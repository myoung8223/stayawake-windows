using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace StayAwake.MsiBuilder
{
    /// <summary>
    /// Builds StayAwake.msi with no third-party tools: makecab.exe (built into Windows) packs the
    /// files, and msi.dll (also built in) writes the installer database.
    ///
    ///   MsiBuilder.exe &lt;folder with StayAwake.exe and StayAwakeService.exe&gt; &lt;output .msi&gt; &lt;version&gt;
    /// </summary>
    static class Program
    {
        // Never change these: they tie upgrades together and identify components across versions.
        const string UpgradeCode = "{E7B3A1C4-2D5F-4A8B-9E6C-1F3D7B2A5C80}";
        const string AgentComponentId = "{6C1E3F2A-8B4D-4E7A-9C21-3A5F7D2B8E41}";
        const string ServiceComponentId = "{A4D2B7C9-1E3F-4B6A-8D5C-2F9E7A1B3C64}";
        const string PolicyComponentId = "{3B8F1D6E-5A2C-4E9B-B7D4-8C1A6F2E9D35}";

        const int Comp64Bit = 256, CompRegistryKeyPath = 4;
        const string PolicyKey = @"SOFTWARE\Policies\StayAwake";

        static int Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.Error.WriteLine("Usage: MsiBuilder <bin folder> <output.msi> <version x.y.z>");
                return 2;
            }
            try
            {
                Build(Path.GetFullPath(args[0]), Path.GetFullPath(args[1]), args[2]);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("MSI build failed: " + ex.Message);
                return 1;
            }
        }

        static void Build(string binDir, string msiPath, string version)
        {
            string agent = Path.Combine(binDir, "StayAwake.exe");
            string service = Path.Combine(binDir, "StayAwakeService.exe");
            foreach (string f in new[] { agent, service })
                if (!File.Exists(f)) throw new FileNotFoundException("Missing " + f);
            if (!Version.TryParse(version, out Version v) || v.Major > 255 || v.Minor > 255)
                throw new ArgumentException("Version must look like 1.0.1");

            string work = Path.Combine(Path.GetTempPath(), "StayAwakeMsi-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                // ---- 1. Cabinet. File keys in the cab must match the File table keys. ----
                string cab = Path.Combine(work, "product.cab");
                string ddf = Path.Combine(work, "product.ddf");
                File.WriteAllText(ddf,
                    ".OPTION EXPLICIT\r\n" +
                    ".Set CabinetNameTemplate=product.cab\r\n" +
                    ".Set DiskDirectoryTemplate=\"" + work + "\"\r\n" +
                    ".Set Cabinet=on\r\n.Set Compress=on\r\n.Set CompressionType=LZX\r\n" +
                    ".Set MaxDiskSize=0\r\n.Set RptFileName=nul\r\n.Set InfFileName=nul\r\n" +
                    "\"" + agent + "\" filAgent\r\n" +
                    "\"" + service + "\" filService\r\n", Encoding.ASCII);
                RunMakecab(ddf, work);
                if (!File.Exists(cab)) throw new InvalidOperationException("makecab did not produce " + cab);

                // ---- 2. Database. ----
                if (File.Exists(msiPath)) File.Delete(msiPath);
                Directory.CreateDirectory(Path.GetDirectoryName(msiPath));
                string productCode = Guid.NewGuid().ToString("B").ToUpperInvariant();
                string packageCode = Guid.NewGuid().ToString("B").ToUpperInvariant();
                string ver = v.Major + "." + v.Minor + "." + Math.Max(0, v.Build);

                using (var db = MsiDatabase.Create(msiPath))
                {
                    foreach (string sql in Schema.CreateStatements) db.Execute(sql);

                    var p = new[] { "Property", "Value" };
                    Row(db, "Property", p, "ProductName", "Stay Awake");
                    Row(db, "Property", p, "ProductCode", productCode);
                    Row(db, "Property", p, "ProductVersion", ver);
                    Row(db, "Property", p, "ProductLanguage", "1033");
                    Row(db, "Property", p, "Manufacturer", "Mike Young");
                    Row(db, "Property", p, "UpgradeCode", UpgradeCode);
                    Row(db, "Property", p, "ALLUSERS", "1");
                    Row(db, "Property", p, "ARPNOMODIFY", "1");
                    Row(db, "Property", p, "ARPNOREPAIR", "1");
                    Row(db, "Property", p, "ARPHELPLINK", "https://github.com/myoung8223/stayawake-windows");
                    Row(db, "Property", p, "MSIRESTARTMANAGERCONTROL", "Disable");
                    // Admin settings (override on the msiexec command line). ALLOWEDGROUP has no default: empty = everyone.
                    Row(db, "Property", p, "IDLEMINUTES", "10");
                    Row(db, "Property", p, "MAXGRACE", "60");
                    Row(db, "Property", p, "HOTKEY", "Ctrl+Alt+S");
                    Row(db, "Property", p, "HISTORYSCOPE", "User");
                    Row(db, "Property", p, "IGNOREINJECTED", "0");
                    Row(db, "Property", p, "SecureCustomProperties", "IDLEMINUTES;MAXGRACE;HOTKEY;HISTORYSCOPE;ALLOWEDGROUP;IGNOREINJECTED;OLDERFOUND;NEWERFOUND");

                    var d = new[] { "Directory", "Directory_Parent", "DefaultDir" };
                    Row(db, "Directory", d, "TARGETDIR", null, "SourceDir");
                    Row(db, "Directory", d, "ProgramFiles64Folder", "TARGETDIR", ".");
                    Row(db, "Directory", d, "INSTALLDIR", "ProgramFiles64Folder", "STAYAW~1|StayAwake");
                    Row(db, "Directory", d, "CommonAppDataFolder", "TARGETDIR", ".");
                    Row(db, "Directory", d, "LOGDIR", "CommonAppDataFolder", "STAYAW~1|StayAwake");

                    var c = new[] { "Component", "ComponentId", "Directory_", "Attributes", "Condition", "KeyPath" };
                    Row(db, "Component", c, "cmpAgent", AgentComponentId, "INSTALLDIR", Comp64Bit, null, "filAgent");
                    Row(db, "Component", c, "cmpService", ServiceComponentId, "INSTALLDIR", Comp64Bit, null, "filService");
                    Row(db, "Component", c, "cmpPolicy", PolicyComponentId, "INSTALLDIR", Comp64Bit | CompRegistryKeyPath, null, "regIdle");

                    Row(db, "Feature", new[] { "Feature", "Feature_Parent", "Title", "Description", "Display", "Level", "Directory_", "Attributes" },
                        "Main", null, "Stay Awake", "Idle lock with teacher grace periods", 1, 1, "INSTALLDIR", 0);
                    var fc = new[] { "Feature_", "Component_" };
                    Row(db, "FeatureComponents", fc, "Main", "cmpAgent");
                    Row(db, "FeatureComponents", fc, "Main", "cmpService");
                    Row(db, "FeatureComponents", fc, "Main", "cmpPolicy");

                    var f = new[] { "File", "Component_", "FileName", "FileSize", "Version", "Language", "Attributes", "Sequence" };
                    Row(db, "File", f, "filAgent", "cmpAgent", "STAYAW~1.EXE|StayAwake.exe", (int)new FileInfo(agent).Length, FileVersion(agent), null, 512, 1);
                    Row(db, "File", f, "filService", "cmpService", "STAYAW~2.EXE|StayAwakeService.exe", (int)new FileInfo(service).Length, FileVersion(service), null, 512, 2);

                    Row(db, "Media", new[] { "DiskId", "LastSequence", "DiskPrompt", "Cabinet", "VolumeLabel", "Source" }, 1, 2, null, "#product.cab", null, null);

                    var r = new[] { "Registry", "Root", "Key", "Name", "Value", "Component_" };
                    Row(db, "Registry", r, "regIdle", 2, PolicyKey, "IdleLockMinutes", "#[IDLEMINUTES]", "cmpPolicy");
                    Row(db, "Registry", r, "regMax", 2, PolicyKey, "MaxGraceMinutes", "#[MAXGRACE]", "cmpPolicy");
                    Row(db, "Registry", r, "regHotkey", 2, PolicyKey, "Hotkey", "[HOTKEY]", "cmpPolicy");
                    Row(db, "Registry", r, "regScope", 2, PolicyKey, "HistoryScope", "[HISTORYSCOPE]", "cmpPolicy");
                    Row(db, "Registry", r, "regGroup", 2, PolicyKey, "AllowedGroup", "[ALLOWEDGROUP]", "cmpPolicy");
                    Row(db, "Registry", r, "regInjected", 2, PolicyKey, "IgnoreInjectedInput", "#[IGNOREINJECTED]", "cmpPolicy");
                    // Name "-" with no value: delete the machine-history key (and subkeys) on uninstall.
                    Row(db, "Registry", r, "regHistory", 2, @"SOFTWARE\StayAwake", "-", null, "cmpService");

                    var rf = new[] { "FileKey", "Component_", "FileName", "DirProperty", "InstallMode" };
                    Row(db, "RemoveFile", rf, "rmLogs", "cmpService", "*", "LOGDIR", 2);
                    Row(db, "RemoveFile", rf, "rmLogDir", "cmpService", null, "LOGDIR", 2);

                    Row(db, "ServiceInstall", new[] { "ServiceInstall", "Name", "DisplayName", "ServiceType", "StartType", "ErrorControl", "LoadOrderGroup", "Dependencies", "StartName", "Password", "Arguments", "Component_", "Description" },
                        "svcInstall", "StayAwakeService", "Stay Awake", 0x10, 2, 1, null, null, null, null, null, "cmpService",
                        "Locks idle sessions and manages Stay Awake grace periods.");
                    // start on install; stop on install, stop + delete on uninstall
                    Row(db, "ServiceControl", new[] { "ServiceControl", "Name", "Event", "Arguments", "Wait", "Component_" },
                        "svcControl", "StayAwakeService", 1 | 2 | 32 | 128, null, 1, "cmpService");

                    var u = new[] { "UpgradeCode", "VersionMin", "VersionMax", "Language", "Attributes", "Remove", "ActionProperty" };
                    Row(db, "Upgrade", u, UpgradeCode, "0.0.0", ver, null, 256 | 512, null, "OLDERFOUND"); // older or same version: replace
                    Row(db, "Upgrade", u, UpgradeCode, ver, null, null, 2, null, "NEWERFOUND");            // newer: detect only

                    var lc = new[] { "Condition", "Description" };
                    Row(db, "LaunchCondition", lc, "Privileged", "Stay Awake must be installed by an administrator.");
                    Row(db, "LaunchCondition", lc, "VersionNT64", "Stay Awake requires 64-bit Windows.");
                    Row(db, "LaunchCondition", lc, "NOT NEWERFOUND", "A newer version of Stay Awake is already installed.");

                    var seq = new[] { "Action", "Condition", "Sequence" };
                    foreach (var a in Schema.ExecuteSequence) Row(db, "InstallExecuteSequence", seq, a.Item1, a.Item2, a.Item3);
                    foreach (var a in Schema.UiSequence) Row(db, "InstallUISequence", seq, a.Item1, a.Item2, a.Item3);

                    db.AddStream("product.cab", cab);
                    db.WriteSummary("Installation Database", "Stay Awake " + ver, "Mike Young",
                        "Idle screen lock with teacher grace periods.", "x64;1033", packageCode);
                    db.Commit();
                }
                Console.WriteLine("Built " + msiPath + " (version " + ver + ", product code " + productCode + ")");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { }
            }
        }

        static void Row(MsiDatabase db, string table, string[] columns, params object[] values) { db.Insert(table, columns, values); }

        static string FileVersion(string path)
        {
            var fvi = FileVersionInfo.GetVersionInfo(path);
            return string.Format("{0}.{1}.{2}.{3}", fvi.FileMajorPart, fvi.FileMinorPart, fvi.FileBuildPart, fvi.FilePrivatePart);
        }

        static void RunMakecab(string ddf, string work)
        {
            string makecab = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "makecab.exe");
            var psi = new ProcessStartInfo(makecab, "/F \"" + ddf + "\"")
            {
                WorkingDirectory = work,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using (var proc = Process.Start(psi))
            {
                string output = proc.StandardOutput.ReadToEnd() + proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                if (proc.ExitCode != 0) throw new InvalidOperationException("makecab failed:\n" + output);
            }
        }
    }
}
