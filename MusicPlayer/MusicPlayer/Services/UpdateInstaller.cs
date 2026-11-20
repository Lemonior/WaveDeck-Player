using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace MusicPlayer.Services;

public sealed record UpdatePlan(int Schema, string Target, string Payload, string Stage, string DataRoot, string Version, string RuntimeId, int ParentId, long ParentStartTicks, string ResumeNonce);

public static class UpdateInstaller
{
    public static bool IsPortableInstallation(string root) => File.Exists(Path.Combine(root, UpdatePackage.ManifestName)) && File.Exists(Path.Combine(root, OperatingSystem.IsWindows() ? "WaveDeck.exe" : "WaveDeck"));
    public static async Task LaunchHelperAsync(PreparedUpdate update, string dataRoot, string resumeNonce, CancellationToken token)
    {
        var target = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (!IsPortableInstallation(target)) throw new InvalidOperationException("Install updates from a portable release folder. Download and extract the latest ZIP first.");
        var expectedExecutable = Path.Combine(target, OperatingSystem.IsWindows() ? "WaveDeck.exe" : "WaveDeck");
        if (!Path.GetFullPath(Environment.ProcessPath!).Equals(expectedExecutable, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidOperationException("Install updates from the portable WaveDeck executable.");
        UpdatePackage.EnsureNoLinks(target);
        var probe = Path.Combine(target, ".wavedeck-write-" + Guid.NewGuid().ToString("N"));
        using (File.Create(probe)) { } File.Delete(probe);
        using var process = Process.GetCurrentProcess();
        var plan = new UpdatePlan(1, target, update.Payload, update.Root, Path.GetFullPath(dataRoot), update.Manifest.Version, update.Manifest.RuntimeId, process.Id, process.StartTime.ToUniversalTime().Ticks, resumeNonce);
        var path = Path.Combine(update.Root, "plan.json"); Storage.WriteAtomic(path, JsonSerializer.Serialize(plan, UpdatePackage.Json));
        var helper = new ProcessStartInfo(Path.Combine(update.Payload, OperatingSystem.IsWindows() ? "WaveDeck.exe" : "WaveDeck")) { UseShellExecute = false, WorkingDirectory = update.Payload, CreateNoWindow = true };
        if (OperatingSystem.IsWindows()) helper.WindowStyle = ProcessWindowStyle.Hidden;
        helper.ArgumentList.Add("--apply-update"); helper.ArgumentList.Add(path);
        using var launched = Process.Start(helper) ?? throw new IOException("The update helper could not be started.");
        var ready = Path.Combine(update.Root, "helper-ready"); var deadline = Environment.TickCount64 + 15000;
        while (!File.Exists(ready))
        {
            token.ThrowIfCancellationRequested();
            if (launched.HasExited || Environment.TickCount64 > deadline) throw new IOException("The update helper did not become ready. The player was left running.");
            await Task.Delay(100, token);
        }
    }
    public static async Task<int> RunHelperAsync(string planPath)
    {
        UpdatePlan? plan = null; var validated = false; var installed = false;
        try
        {
            UpdatePackage.EnsureNoLinks(planPath);
            if (new FileInfo(planPath).Length > 65536) throw new InvalidDataException("Invalid update plan.");
            plan = JsonSerializer.Deserialize<UpdatePlan>(await File.ReadAllTextAsync(planPath), UpdatePackage.Json) ?? throw new InvalidDataException("Invalid update plan.");
            ValidatePlan(plan);
            if (Path.GetFullPath(planPath) != Path.Combine(plan.Stage, "plan.json") || !Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar).Equals(plan.Payload, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("Invalid update helper location.");
            validated = true;
            var manifest = await ReadManifestAsync(plan.Payload, plan.Version, plan.RuntimeId);
            foreach (var file in manifest.Files) await UpdatePackage.VerifyFileAsync(plan.Payload, file, CancellationToken.None);
            Process? parent = null;
            try { parent = Process.GetProcessById(plan.ParentId); } catch (ArgumentException) { }
            if (parent != null)
            {
                using (parent)
                {
                    var expected = Path.Combine(plan.Target, plan.RuntimeId == "win-x64" ? "WaveDeck.exe" : "WaveDeck");
                    if (parent.StartTime.ToUniversalTime().Ticks != plan.ParentStartTicks || !Path.GetFullPath(parent.MainModule!.FileName).Equals(expected, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) throw new InvalidDataException("The update parent process did not match.");
                    await File.WriteAllTextAsync(Path.Combine(plan.Stage, "helper-ready"), "ready");
                    using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2)); await parent.WaitForExitAsync(timeout.Token);
                }
            }
            else await File.WriteAllTextAsync(Path.Combine(plan.Stage, "helper-ready"), "ready");
            await Task.Delay(150);
            var backup = await ApplyFilesAsync(plan.Target, plan.Payload, manifest, CancellationToken.None);
            installed = true;
            UpdateState.WriteOutcome(plan.DataRoot, new(true, plan.Version, "The application update was installed.", backup));
            Restart(plan); return 0;
        }
        catch (Exception ex)
        {
            if (plan != null && validated)
            {
                try { UpdateState.WriteOutcome(plan.DataRoot, new(installed, plan.Version, installed ? "The update was installed. Please start WaveDeck if it did not reopen." : ex is OperationCanceledException ? "The update timed out waiting for the player to close." : "The update could not be installed. Check the application's .wavedeck-backups folder before retrying.", null)); } catch { }
                // Never start a second player while the original process is still alive.
                var alive = false; try { using var parent = Process.GetProcessById(plan.ParentId); alive = !parent.HasExited && parent.StartTime.ToUniversalTime().Ticks == plan.ParentStartTicks; } catch { }
                if (!alive) try { Restart(plan); } catch { }
            }
            return 1;
        }
    }
    private static void ValidatePlan(UpdatePlan plan)
    {
        if (plan.Schema != 1 || plan.ParentId <= 0 || plan.ParentStartTicks <= 0 || !Guid.TryParseExact(plan.ResumeNonce, "N", out _) || !Path.IsPathFullyQualified(plan.Target) || !Path.IsPathFullyQualified(plan.Payload) || !Path.IsPathFullyQualified(plan.Stage) || !Path.IsPathFullyQualified(plan.DataRoot)) throw new InvalidDataException("Invalid update plan.");
        foreach (var root in new[] { plan.Target, plan.Payload, plan.Stage, plan.DataRoot }) UpdatePackage.EnsureNoLinks(root);
        if (Path.GetFullPath(plan.Payload) != Path.Combine(Path.GetFullPath(plan.Stage), "payload") || Path.GetFullPath(plan.Target).Equals(Path.GetFullPath(plan.Payload), StringComparison.OrdinalIgnoreCase) || Path.GetFullPath(plan.Stage).StartsWith(Path.GetFullPath(plan.Target).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The update staging folder is invalid.");
        var relative = Path.GetRelativePath(UpdateState.Folder(plan.DataRoot), plan.Stage);
        if (!Guid.TryParseExact(relative, "N", out _)) throw new InvalidDataException("The update staging folder is invalid.");
        if (!IsPortableInstallation(plan.Target) || plan.RuntimeId != AppReleaseClient.RuntimeId) throw new InvalidDataException("The update target or platform is invalid.");
    }
    private static void Restart(UpdatePlan plan)
    {
        var start = new ProcessStartInfo(Path.Combine(plan.Target, plan.RuntimeId == "win-x64" ? "WaveDeck.exe" : "WaveDeck")) { UseShellExecute = false, WorkingDirectory = plan.Target };
        if (OperatingSystem.IsWindows()) start.WindowStyle = ProcessWindowStyle.Hidden;
        start.ArgumentList.Add("--data-dir"); start.ArgumentList.Add(plan.DataRoot); start.ArgumentList.Add("--resume-update"); start.ArgumentList.Add(plan.ResumeNonce);
        using var process = Process.Start(start) ?? throw new IOException("WaveDeck could not be restarted.");
    }
    internal static async Task<UpdateManifest> ReadManifestAsync(string root, string? version = null, string? runtimeId = null)
    {
        var path = Path.Combine(root, UpdatePackage.ManifestName); UpdatePackage.EnsureNoLinks(path);
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("The application file manifest is too large.");
        var result = JsonSerializer.Deserialize<UpdateManifest>(await File.ReadAllTextAsync(path), UpdatePackage.Json) ?? throw new InvalidDataException("Invalid application file manifest.");
        UpdatePackage.ValidateManifest(result, version, runtimeId); return result;
    }
    internal static async Task<string> ApplyFilesAsync(string target, string payload, UpdateManifest manifest, CancellationToken token)
    {
        UpdatePackage.EnsureNoLinks(target); UpdatePackage.EnsureNoLinks(payload);
        UpdatePackage.ValidateManifest(manifest);
        var previous = await ReadManifestAsync(target, runtimeId: manifest.RuntimeId);
        if (AppReleaseClient.ParseVersion(manifest.Version) <= AppReleaseClient.ParseVersion(previous.Version)) throw new InvalidDataException("Updates must be newer than the installed version.");
        foreach (var file in manifest.Files) await UpdatePackage.VerifyFileAsync(payload, file, token);
        var backup = Path.Combine(target, ".wavedeck-backups", Guid.NewGuid().ToString("N")); UpdatePackage.EnsureNoLinks(backup); Directory.CreateDirectory(backup);
        var changes = new List<(string Destination, string Backup, bool Installed)>(); var temporary = new List<string>();
        try
        {
            foreach (var file in manifest.Files)
            {
                token.ThrowIfCancellationRequested();
                var destination = UpdatePackage.SafePath(target, file.Path); UpdatePackage.EnsureNoLinks(destination);
                if (Directory.Exists(destination)) throw new IOException("An update file conflicts with an existing folder.");
                if (File.Exists(destination))
                {
                    await using var existing = File.OpenRead(destination);
                    if (existing.Length == file.Length && Convert.ToHexString(await SHA256.HashDataAsync(existing, token)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                }
                Install(file.Path, UpdatePackage.SafePath(payload, file.Path));
            }
            var next = manifest.Files.Select(file => file.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var obsolete in previous.Files.Where(file => !next.Contains(file.Path)))
            {
                var destination = UpdatePackage.SafePath(target, obsolete.Path); UpdatePackage.EnsureNoLinks(destination);
                if (!File.Exists(destination)) continue;
                var saved = UpdatePackage.SafePath(backup, obsolete.Path); Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                Retry(() => File.Move(destination, saved)); changes.Add((destination, saved, false));
            }
            Install(UpdatePackage.ManifestName, Path.Combine(payload, UpdatePackage.ManifestName));
            Storage.WriteAtomic(Path.Combine(backup, "transaction.json"), JsonSerializer.Serialize(new { previous.Version, UpdatedTo = manifest.Version, Files = changes.Select(change => new { change.Destination, change.Backup }) }, UpdatePackage.Json));
            return backup;
        }
        catch
        {
            var failed = false;
            foreach (var change in changes.AsEnumerable().Reverse())
            {
                try { if (change.Installed && File.Exists(change.Destination)) File.Delete(change.Destination); if (File.Exists(change.Backup)) File.Move(change.Backup, change.Destination); }
                catch { failed = true; }
            }
            if (failed) throw new IOException("The update failed and could not restore every file. The backup is in the application's .wavedeck-backups folder.");
            throw;
        }
        finally { foreach (var path in temporary) try { if (File.Exists(path)) File.Delete(path); } catch { } }
        void Install(string relative, string source)
        {
            var destination = UpdatePackage.SafePath(target, relative); UpdatePackage.EnsureNoLinks(destination); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            var staged = destination + ".wavedeck-new-" + Guid.NewGuid().ToString("N"); temporary.Add(staged); File.Copy(source, staged);
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(staged, File.GetUnixFileMode(source));
            var saved = UpdatePackage.SafePath(backup, relative); Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
            if (File.Exists(destination)) Retry(() => File.Move(destination, saved));
            changes.Add((destination, saved, false));
            Retry(() => File.Move(staged, destination)); changes[^1] = (destination, saved, true);
        }
    }
    private static void Retry(Action action)
    {
        for (var attempt = 0; ; attempt++)
        { try { action(); return; } catch (IOException) when (attempt < 20) { Thread.Sleep(100); } }
    }
}
