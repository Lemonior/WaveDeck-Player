# GitHub release preparation - 2026-11-19

- Central application version: `Directory.Build.props`, `1.0.0`. Both projects resolve this version; both published assemblies report `1.0.0.0`. Windows About and Linux title/About read assembly informational metadata.
- Both self-contained Release publishes completed successfully using .NET 10.0.12. Packaging verified required launchers, runtime metadata, tools, licenses, the matching SoundTouch.Net source archive, and forward-slash ZIP entries. Packages include `VERSION.txt`; a SHA-256 manifest accompanies the ZIPs.
- Windows native self-tests: **63/63 passed**, exit code 0, from isolated release staging. Evidence: local ignored `artifacts/release-validation/windows/results.json`, `summary.txt`, and `environment.json`.
- Linux runtime tests could not start: Ubuntu/WSL reports `Wsl/Service/CreateInstance/MountDisk/ERROR_PATH_NOT_FOUND` for its registered `ext4.vhdx`. Linux compilation and archive checks passed; native Linux execution was not verified in this run.
- All PowerShell scripts parse. An intentionally mismatched `v9.9.9` tag is rejected before building. Ignore checks cover Windows, Linux, future Android output, archives, tools, artifacts and local `AGENTS.md`.
- Initial source inventory: approximately 0.75 MB. Generated builds, downloaded executables, local agent instructions and the unrelated shell utility are excluded. A focused source scan found no GitHub/Google token or private-key signatures.
- Packaging used isolated `artifacts/release-staging/` folders. The existing Windows listening process and live output folders were preserved. No personal account-session or listening-quality verification was performed.

The tag-driven GitHub workflow builds its own packages from the tagged commit. Consult that Actions run and the published assets for cloud-build results and final checksums; local preparation does not establish a successful cloud run.
