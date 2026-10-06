using CameraProbe;
using System.Runtime.InteropServices;

int failed = 0;
var tests = new (string Name, Action Run)[]
{
    ("engine build location follows relocated RIP-relative code", () =>
    {
        byte[] Pattern(int displacement)
        {
            byte[] code = Convert.FromHexString("890500000000488D0D11223344FF1555667788488B0D");
            BitConverter.GetBytes(displacement).CopyTo(code, 2);
            return code;
        }
        if (EngineBuildLocator.FindOffset(new[] { (0x1000, Pattern(0x2FFA)) }, 0x6000) != 0x4000)
            throw new Exception("Forward displacement resolved incorrectly");
        if (EngineBuildLocator.FindOffset(new[] { (0x5000, Pattern(-0x1006)) }, 0x6000) != 0x4000)
            throw new Exception("Signed backward displacement resolved incorrectly");
        Reject(() => EngineBuildLocator.FindOffset(new[] { (0x1000, Pattern(0x2FFA)), (0x2000, Pattern(0x1FFA)) }, 0x6000));
        Reject(() => EngineBuildLocator.FindOffset(new[] { (0x1000, Pattern(int.MaxValue)) }, 0x6000));
        Reject(() => EngineBuildLocator.FindOffset(new[] { (0x1000, new byte[21]) }, 0x6000));
    }),
    ("masked byte pattern requires exactly one match", () =>
    {
        var match = BytePattern.Parse("AA ?? CC").FindUnique(new[] { (0x1000, new byte[] { 0, 0xAA, 0x12, 0xCC }) });
        if (match.Rva != 0x1001 || Convert.ToHexString(match.Bytes) != "AA12CC")
            throw new Exception("Masked match was resolved incorrectly");
        Reject(() => BytePattern.Parse("AA ?? CC").FindUnique(new[] { (0x1000, new byte[] { 0xAA, 0x12, 0 }) }));
        Reject(() => BytePattern.Parse("AA ?? CC").FindUnique(new[]
        {
            (0x1000, new byte[] { 0xAA, 0x12, 0xCC }),
            (0x2000, new byte[] { 0xAA, 0x34, 0xCC })
        }));
    }),
    ("runtime player code locator resolves inspected 14189 instructions", () =>
    {
        var layout = PlayerCodeLocator.Locate(CodeFixture(decoyCameraEntry: true), 0x2800000, 5208, 844);
        if (layout.CameraEntryRva != 0x8827BC || layout.CameraDeathLoadRva != 0x8827E7 ||
            layout.CameraCompareRva != 0x88280C || layout.MovementHealthRva != 0x8C4ADE ||
            layout.Fingerprint.Length != 64)
            throw new Exception("Runtime code layout was resolved incorrectly");
    }),
    ("runtime player code locator rejects ambiguous and incompatible instructions", () =>
    {
        Reject(() => PlayerCodeLocator.Locate(CodeFixture(duplicateMovement: true), 0x2800000, 5208, 844));
        Reject(() => PlayerCodeLocator.Locate(CodeFixture(), 0x2800000, 5209, 844));
        Reject(() => PlayerCodeLocator.Locate(CodeFixture(), 0x2800000, 5208, 845));
        Reject(() => PlayerCodeLocator.Locate(CodeFixture(outOfRangeCall: true), 0x2800000, 5208, 844));
        Reject(() => PlayerCodeLocator.Locate(CodeFixture(duplicateCameraCandidate: true), 0x2800000, 5208, 844));
    }),
    ("automatic health recovery does not carry into respawn or another round", () =>
    {
        var s = new Snapshot { Team = 2, Health = 10000, LifeState = 2, PawnIsAlive = false,
            PawnRuntimeClass = ".?AVC_CSPlayerPawn@@", ControllerPawn = 123, PlayerPawn = 123,
            RoundStartCount = 1 };
        if (!MovementExperimentPolicy.SameContext(s, 123, 2, 1)) throw new Exception("Lost established context");
        s.RoundStartCount = 2;
        if (MovementExperimentPolicy.SameContext(s, 123, 2, 1)) throw new Exception("Accepted new round");
        s.RoundStartCount = 1; s.LifeState = 0;
        if (MovementExperimentPolicy.SameContext(s, 123, 2, 1)) throw new Exception("Accepted respawn");
        if (!MovementExperimentPolicy.ShouldRestore(1, true, true, temporaryHealth: 1))
            throw new Exception("Legacy experiment restoration lost");
    }),
    ("movement probe requires zero health and an active camera recovery", () =>
    {
        var s = new Snapshot { Team = 2, Health = 0, LifeState = 2, PawnIsAlive = false,
            PawnRuntimeClass = ".?AVC_CSPlayerPawn@@", ControllerPawn = 123, PlayerPawn = 123,
            RoundStartCount = 1, FreezeTime = false, DeathTime = CameraExperimentPolicy.TemporaryDeathTime };
        MovementExperimentPolicy.Require(s);
        s.Health = 100; Reject(() => MovementExperimentPolicy.Require(s));
        s.Health = 0; s.DeathTime = 123; Reject(() => MovementExperimentPolicy.Require(s));
        s.DeathTime = CameraExperimentPolicy.TemporaryDeathTime; s.FreezeTime = true;
        Reject(() => MovementExperimentPolicy.Require(s));
    }),
    ("movement probe preserves network updates and rejects ambiguous restoration", () =>
    {
        if (!MovementExperimentPolicy.ShouldRestore(10000, true, true)) throw new Exception("Lost temporary health");
        if (MovementExperimentPolicy.ShouldRestore(0, false, false)) throw new Exception("Already restored");
        if (MovementExperimentPolicy.ShouldRestore(75, true, false)) throw new Exception("Would overwrite network health");
        Reject(() => MovementExperimentPolicy.ShouldRestore(10000, true, false));
        Reject(() => MovementExperimentPolicy.ShouldRestore(256, false, true));
    }),
    ("camera recovery survives recorded health depletion only in an established bug", () =>
    {
        var recovery = new CameraRecoveryState();
        var s = new Snapshot { Team = 3, Health = 0, LifeState = 2, PawnIsAlive = false,
            PawnRuntimeClass = ".?AVC_CSPlayerPawn@@", ControllerPawn = 20349130,
            PlayerPawn = 20349130, RoundStartCount = 11 };
        Reject(() => recovery.Require(s));
        s.Health = 8; recovery.Require(s);
        s.Health = 0; recovery.Require(s);
        s.RoundStartCount = 12; Reject(() => recovery.Require(s));
        s.RoundStartCount = 11; Reject(() => recovery.Require(s));
        s.Health = 100; recovery.Require(s);
        s.Health = 0; s.ControllerPawn = 123; s.PlayerPawn = 123;
        Reject(() => recovery.Require(s));
    }),
    ("camera recovery forgets the bug on respawn or observer transition", () =>
    {
        foreach (bool observer in new[] { false, true })
        {
            var recovery = new CameraRecoveryState();
            var s = new Snapshot { Team = 2, Health = 100, LifeState = 2, PawnIsAlive = false,
                PawnRuntimeClass = ".?AVC_CSPlayerPawn@@", ControllerPawn = 123, PlayerPawn = 123,
                RoundStartCount = 1 };
            recovery.Require(s);
            if (observer) s.PawnRuntimeClass = ".?AVC_CSObserverPawn@@";
            else s.LifeState = 0;
            Reject(() => recovery.Require(s));
            s.PawnRuntimeClass = ".?AVC_CSPlayerPawn@@"; s.LifeState = 2; s.Health = 0;
            Reject(() => recovery.Require(s));
        }
    }),
    ("active camera transaction ends on team or round change even at positive health", () =>
    {
        foreach (bool changeTeam in new[] { false, true })
        {
            var recovery = new CameraRecoveryState();
            var original = new Snapshot { Team = 2, Health = 100, LifeState = 2, PawnIsAlive = false,
                PawnRuntimeClass = ".?AVC_CSPlayerPawn@@", ControllerPawn = 123, PlayerPawn = 123,
                RoundStartCount = 1 };
            recovery.Require(original);
            var next = new Snapshot { Team = changeTeam ? (byte)3 : (byte)2, Health = 100,
                LifeState = 2, PawnIsAlive = false, PawnRuntimeClass = ".?AVC_CSPlayerPawn@@",
                ControllerPawn = 123, PlayerPawn = 123, RoundStartCount = changeTeam ? 1 : 2 };
            Reject(() => recovery.RequireContinuation(original, next));
        }
    }),
    ("recovery distinguishes stable recycled handles from uncertain identity", () =>
    {
        ulong Pointer(ulong a) => a switch { 0x10010 => 0x20000, 0x20070 => 0x30000, 0x30010 => 0x20070, _ => throw new Exception("Unexpected read") };
        uint Stable(ulong a) => a == 0x20080 ? 0x10001U : 0U;
        if (LocalPawnResolver.Resolve(0x10000, 0x8001, Pointer, Stable, 16, retiredIsMissing: true) != 0)
            throw new Exception("Recycled entity still matched");
        int serialReads = 0;
        uint Changing(ulong a) => a == 0x20080 ? (++serialReads == 1 ? 0x10001U : 0x8001U) : 0U;
        Reject(() => LocalPawnResolver.Resolve(0x10000, 0x8001, Pointer, Changing, 16, retiredIsMissing: true));
    }),
    ("uncertain partial camera writes retain recovery work", () =>
    {
        byte[] partial = BitConverter.GetBytes(86.015625f);
        partial[0] = BitConverter.GetBytes(1000000000f)[0];
        Reject(() => CameraExperimentPolicy.ShouldRestore(BitConverter.ToSingle(partial), 86.015625f, false));
        if (!CameraExperimentPolicy.ShouldRestore(1000000000f, 86.015625f, false)) throw new Exception("Owned write was not restored");
        if (CameraExperimentPolicy.ShouldRestore(86.015625f, 86.015625f, false)) throw new Exception("Untouched value needs no write");
        if (CameraExperimentPolicy.ShouldRestore(90f, 86.015625f, true)) throw new Exception("Would replace game update");
    }),
    ("round trip reapplies image values reset by team change", () =>
    {
        (int Fullbright, float Freeze) values = (0, 3);
        bool switched = false;
        ImageCommands.RoundTrip(() => values = (1, 1000), () => { switched = true; values = (0, 3); },
            () => { if (!switched) throw new Exception("Missing switch"); });
        if (values != (1, 1000)) throw new Exception("Team reset left image settings disabled");
    }),
    ("failed switch does not report post-switch images applied", () =>
    {
        int applications = 0;
        Reject(() => ImageCommands.RoundTrip(() => applications++, () => throw new InvalidOperationException(), () => { }));
        if (applications != 1) throw new Exception("Continued after failed team switch");
    }),
    ("image recovery still applies when team switching is unavailable", () =>
    {
        int applications = 0, switches = 0;
        bool switched = ImageCommands.RecoverOrRoundTrip(() => applications++, () => false,
            () => switches++, () => throw new Exception("Validated a skipped switch"));
        if (switched || applications != 1 || switches != 0)
            throw new Exception("Image-only recovery did not stop after applying values");
    }),
    ("camera experiment rejects ordinary alive and dead players", () =>
    {
        var s = new Snapshot { Team = 2, Health = 100, LifeState = 2, PawnIsAlive = false,
            PawnRuntimeClass = ".?AVC_CSPlayerPawn@@", ControllerPawn = 123, PlayerPawn = 123 };
        CameraExperimentPolicy.RequireBug(s);
        s.Health = 0; Reject(() => CameraExperimentPolicy.RequireBug(s));
        s.Health = 100; s.LifeState = 0; Reject(() => CameraExperimentPolicy.RequireBug(s));
        s.LifeState = 2; s.PawnIsAlive = true; Reject(() => CameraExperimentPolicy.RequireBug(s));
        s.PawnIsAlive = false; s.Team = 0; Reject(() => CameraExperimentPolicy.RequireBug(s));
    }),
    ("camera rollback preserves game changes", () =>
    {
        if (!CameraExperimentPolicy.OwnsValue(1000000000f)) throw new Exception("Lost owned value");
        if (CameraExperimentPolicy.OwnsValue(70f) || CameraExperimentPolicy.OwnsValue(float.NaN))
            throw new Exception("Would overwrite a game update");
    }),
    ("console submission waits for text then guards physical Enter", () =>
    {
        var events = new List<string>();
        ConsoleSubmission.Execute("spec_freeze_time 1000", s => events.Add(s),
            ms => events.Add($"wait:{ms}"), () => events.Add("guard"), () => events.Add("enter"));
        if (!events.SequenceEqual(new[] { "spec_freeze_time 1000", "wait:120", "guard", "enter" }))
            throw new Exception("Enter was sent before text processing or guard");
    }),
    ("console submission never sends Enter after cancellation during wait", () =>
    {
        bool entered = false;
        Cancelled(() => ConsoleSubmission.Execute("mat_fullbright 1", _ => { },
            _ => throw new OperationCanceledException(), () => { }, () => entered = true));
        if (entered) throw new Exception("Submitted after cancellation");
    }),
    ("F6 applies and verifies image commands on every press", () =>
    {
        var sent = new List<string>();
        (int Fullbright, float Freeze) values = (1, 1000);
        void Send(string command)
        {
            sent.Add(command);
            values = command.StartsWith("spec_freeze_time 1001;", StringComparison.Ordinal) ? (1, 1001) : (1, 1000);
        }
        ImageCommands.Apply(Send, () => values, _ => { });
        ImageCommands.Apply(Send, () => values, _ => { });
        if (sent.Count != 4 || sent.Count(s => s == ImageCommands.Target) != 2)
            throw new Exception("Did not apply target values on both presses");
    }),
    ("key release retries once and reports persistent failure", () =>
    {
        int attempts = 0;
        Native.ReleaseInputs([new Native.Input { Type = 1, Flags = 2 }], _ => ++attempts == 1 ? 0U : 1U);
        if (attempts != 2) throw new Exception("Did not retry release");
        Reject(() => Native.ReleaseInputs([new Native.Input { Type = 1, Flags = 2 }], _ => 0));
    }),
    ("already enabled values cannot impersonate a working console", () =>
    {
        int sent = 0;
        Reject(() => ImageCommands.Apply(_ => sent++, () => (1, 1000f), _ => { }));
        if (sent != 1) throw new Exception("Continued after console verification failed");
    }),
    ("cancelled image command application stops before target command", () =>
    {
        int sent = 0;
        Cancelled(() => ImageCommands.Apply(_ => sent++, () => (0, 3f), _ => throw new OperationCanceledException()));
        if (sent != 1) throw new Exception("Applied more commands after cancellation");
    }),
    ("CT switches to T and back", () => Equal(new ushort[] { 2, 3 }, SwitchPolicy.Teams(3, true, true, 75))),
    ("T switches to CT and back", () => Equal(new ushort[] { 3, 2 }, SwitchPolicy.Teams(2, true, true, 75))),
    ("background input rejected", () => Reject(() => SwitchPolicy.Teams(3, true, false, 75))),
    ("live round input rejected", () => Reject(() => SwitchPolicy.Teams(3, false, true, 75))),
    ("spectator input rejected", () => Reject(() => SwitchPolicy.Teams(1, true, true, 75))),
    ("zero delay rejected", () => Reject(() => SwitchPolicy.Teams(3, true, true, 0))),
    ("long delay rejected", () => Reject(() => SwitchPolicy.Teams(3, true, true, 1001))),
    ("build mismatch rejected", () => Reject(() => Validation.Build(14185, 14184))),
    ("build match accepted", () => Validation.Build(14185, 14185)),
    ("ConVar profiles accept only inspected builds", () =>
    {
        _ = ConVarLayout.ForBuild(14185);
        _ = ConVarLayout.ForBuild(14186);
        _ = ConVarLayout.ForBuild(14188);
        _ = ConVarLayout.ForBuild(14189);
        Reject(() => ConVarLayout.ForBuild(14187));
        Reject(() => ConVarLayout.ForBuild(14190));
        Reject(() => ConVarLayout.ForBuild(0));
    }),
    ("player code profiles use inspected instructions for each supported build", () =>
    {
        var old = PlayerCodeLayout.ForBuild(14186);
        if (old.CameraEntryRva != 0x882F7C || old.MovementHealthRva != 0x8C529E)
            throw new Exception("14186 code profile changed");
        var current = PlayerCodeLayout.ForBuild(14188);
        if (current.CameraEntryRva != 0x8827BC || current.CameraDeathLoadRva != 0x8827E7 ||
            current.CameraCompareRva != 0x88280C || current.MovementHealthRva != 0x8C4ADE)
            throw new Exception("14188 code profile does not match inspected instructions");
        var latest = PlayerCodeLayout.ForBuild(14189);
        if (latest.CameraEntryRva != 0x8827BC || latest.CameraDeathLoadRva != 0x8827E7 ||
            latest.CameraCompareRva != 0x88280C || latest.MovementHealthRva != 0x8C4ADE ||
            latest.CameraCompareHex != "E82F9B8DFF0F2F05D4B2380176204D8BCF4D8BC6488BD6488BCFE8D5F2FFFF")
            throw new Exception("14189 code profile does not match inspected instructions");
        Reject(() => PlayerCodeLayout.ForBuild(14187));
        Reject(() => PlayerCodeLayout.ForBuild(14190));
    }),
    ("player schema selection separates hotfixes that share an engine build", () =>
    {
        string root = Path.Combine(Path.GetTempPath(), $"camera-probe-{Guid.NewGuid():N}");
        const string legacyHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        const string oldHotfixHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
        const string newHotfixHash = "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
        void Profile(string directory, int build, string? hash)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "info.json"), $"{{\"build_number\":{build}}}");
            File.WriteAllText(Path.Combine(directory, "provenance.json"), hash is null
                ? "{\"kind\":\"local-read-only-dump\"}"
                : $"{{\"client_sha256\":\"{hash}\"}}");
            File.WriteAllText(Path.Combine(directory, "client_dll.json"), "{}");
            File.WriteAllText(Path.Combine(directory, "offsets.json"), "{}");
        }
        try
        {
            Profile(root, 14186, null); // Real 14186 metadata predates client hashing.
            string oldHotfix = Path.Combine(root, "builds", "14188", oldHotfixHash);
            string newHotfix = Path.Combine(root, "builds", "14188", newHotfixHash);
            string latestBuild = Path.Combine(root, "builds", "14189", legacyHash);
            Profile(oldHotfix, 14188, oldHotfixHash);
            Profile(newHotfix, 14188, newHotfixHash);
            Profile(latestBuild, 14189, legacyHash);
            if (ReferenceProfile.Resolve(root, 14186, legacyHash.ToUpperInvariant()) != root)
                throw new Exception("Legacy schema was not selected by hash");
            if (ReferenceProfile.Resolve(root, 14188, oldHotfixHash) != oldHotfix)
                throw new Exception("First hotfix schema was not selected");
            if (ReferenceProfile.Resolve(root, 14188, newHotfixHash) != newHotfix)
                throw new Exception("Second hotfix schema was not selected");
            if (ReferenceProfile.Resolve(root, 14189, legacyHash) != latestBuild)
                throw new Exception("Latest build schema was not selected");
            Reject(() => ReferenceProfile.Resolve(root, 14188, legacyHash));
            Reject(() => ReferenceProfile.Resolve(root, 14189, newHotfixHash));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }),
    ("automatic profile rejects untrusted dumper and failed execution", () =>
    {
        string root = TempDirectory(), tool = Path.Combine(root, "dumper.exe");
        File.WriteAllText(tool, "pinned test tool");
        string toolHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tool))).ToLowerInvariant();
        try
        {
            Reject(() => AutomaticProfile.Resolve(Path.Combine(root, "cache"), tool, new string('0', 64),
                15000, new string('a', 64), () => true, (_, _, _) => throw new Exception("Runner should not start")));
            Reject(() => AutomaticProfile.Resolve(Path.Combine(root, "cache"), tool, toolHash,
                15000, new string('b', 64), () => true, (_, _, _) => new DumperResult(7, false, "", "failed")));
            Reject(() => AutomaticProfile.Resolve(Path.Combine(root, "cache"), tool, toolHash,
                15000, new string('c', 64), () => true, (_, _, _) => new DumperResult(-1, true, "", "timeout")));
        }
        finally { Directory.Delete(root, true); }
    }),
    ("automatic profile publishes atomically only for the same game process", () =>
    {
        string root = TempDirectory(), cache = Path.Combine(root, "cache"), tool = Path.Combine(root, "dumper.exe");
        File.WriteAllText(tool, "pinned test tool");
        string toolHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tool))).ToLowerInvariant();
        const int build = 15001;
        string hash = new('d', 64);
        DumperResult Run(string _, string output, TimeSpan __) { WriteDiscoveryProfile(output, build); return new(0, false, "ok", ""); }
        try
        {
            Reject(() => AutomaticProfile.Resolve(cache, tool, toolHash, build, hash, () => false, Run));
            string final = Path.Combine(cache, build.ToString(), hash);
            if (Directory.Exists(final)) throw new Exception("Changed process output became authoritative");
            var resolved = AutomaticProfile.Resolve(cache, tool, toolHash, build, hash, () => true, Run);
            if (resolved.Source != ProfileSource.Automatic || resolved.Path != final ||
                !File.Exists(Path.Combine(final, "provenance.json")))
                throw new Exception("Automatic profile was not published");
        }
        finally { Directory.Delete(root, true); }
    }),
    ("automatic profile validates output and replaces an incomplete cache", () =>
    {
        string root = TempDirectory(), cache = Path.Combine(root, "cache"), tool = Path.Combine(root, "dumper.exe");
        File.WriteAllText(tool, "pinned test tool");
        string toolHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tool))).ToLowerInvariant();
        try
        {
            void Rejected(char hashChar, Action<string> write) => Reject(() => AutomaticProfile.Resolve(cache, tool, toolHash,
                15002, new string(hashChar, 64), () => true, (_, output, _) => { write(output); return new(0, false, "", ""); }));
            Rejected('e', output => { Directory.CreateDirectory(output); File.WriteAllText(Path.Combine(output, "info.json"), "{}"); });
            Rejected('f', output => WriteDiscoveryProfile(output, 15003));
            Rejected('1', output => WriteDiscoveryProfile(output, 15002, globalOffset: -1));

            string hash = new('2', 64), incomplete = Path.Combine(cache, "15002", hash);
            Directory.CreateDirectory(incomplete);
            File.WriteAllText(Path.Combine(incomplete, "info.json"), "incomplete");
            var result = AutomaticProfile.Resolve(cache, tool, toolHash, 15002, hash, () => true,
                (_, output, _) => { WriteDiscoveryProfile(output, 15002); return new(0, false, "", ""); });
            if (result.Path != incomplete || !Directory.EnumerateDirectories(Path.GetDirectoryName(incomplete)!, hash + ".invalid-*").Any())
                throw new Exception("Incomplete cache was reused or lost without quarantine");
        }
        finally { Directory.Delete(root, true); }
    }),
    ("valid automatic cache is reused and reviewed profile takes precedence", () =>
    {
        string root = TempDirectory(), reviewed = Path.Combine(root, "reference"), cache = Path.Combine(root, "cache");
        string tool = Path.Combine(root, "dumper.exe"); File.WriteAllText(tool, "pinned test tool");
        string toolHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(tool))).ToLowerInvariant();
        int runs = 0;
        try
        {
            const int autoBuild = 15004; string autoHash = new('3', 64);
            string autoPath = Path.Combine(cache, autoBuild.ToString(), autoHash);
            WriteDiscoveryProfile(autoPath, autoBuild, autoHash);
            var cached = AutomaticProfile.Resolve(cache, tool, toolHash, autoBuild, autoHash, () => true,
                (_, _, _) => { runs++; throw new Exception("Valid cache should not run dumper"); });
            if (cached.Path != autoPath || runs != 0) throw new Exception("Valid automatic cache was not reused");

            const int reviewedBuild = 15005; string reviewedHash = new('4', 64);
            string reviewedPath = Path.Combine(reviewed, "builds", reviewedBuild.ToString(), reviewedHash);
            WriteDiscoveryProfile(reviewedPath, reviewedBuild, reviewedHash);
            var selected = ReferenceProfile.ResolveOrDiscover(reviewed, cache, Path.Combine(root, "missing.exe"),
                reviewedBuild, reviewedHash, () => true, (_, _, _) => throw new Exception("Reviewed profile should win"));
            if (selected.Source != ProfileSource.Reviewed || selected.Path != reviewedPath)
                throw new Exception("Reviewed profile did not take precedence");

            string forcedPath = Path.Combine(cache, reviewedBuild.ToString(), reviewedHash);
            WriteDiscoveryProfile(forcedPath, reviewedBuild, reviewedHash);
            var forced = ReferenceProfile.ResolveOrDiscover(reviewed, cache, Path.Combine(root, "missing.exe"),
                reviewedBuild, reviewedHash, () => true,
                (_, _, _) => throw new Exception("Forced discovery should reuse the automatic cache"),
                forceAutomatic: true);
            if (forced.Source != ProfileSource.Automatic || forced.Path != forcedPath || runs != 0)
                throw new Exception("Forced discovery did not bypass the reviewed profile");
        }
        finally { Directory.Delete(root, true); }
    }),
    ("compatibility startup report identifies reviewed and automatic provenance", () =>
    {
        string hash = new('a', 64), code = new('b', 64);
        string reviewed = CompatibilityReport.Format(ProfileSource.Reviewed, 14189, hash, code);
        string automatic = CompatibilityReport.Format(ProfileSource.Automatic, 14189, hash, code);
        if (!reviewed.Contains("source=reviewed", StringComparison.Ordinal) ||
            !automatic.Contains("source=automatic", StringComparison.Ordinal) ||
            !reviewed.Contains("build=14189", StringComparison.Ordinal) ||
            !reviewed.Contains(hash[..12], StringComparison.Ordinal) ||
            !reviewed.Contains(code[..12], StringComparison.Ordinal))
            throw new Exception("Compatibility provenance is not visible at startup");
    }),
    ("compatibility context extracts required positions equally from reviewed and automatic profiles", () =>
    {
        string root = TempDirectory(); const int build = 16000; string hash = new('5', 64);
        try
        {
            string reviewedPath = Path.Combine(root, "reviewed"), automaticPath = Path.Combine(root, "automatic");
            WriteDiscoveryProfile(reviewedPath, build, hash, conVarOffset: 123456);
            WriteDiscoveryProfile(automaticPath, build, hash, conVarOffset: 123456);
            var reviewed = CompatibilityContext.Load(new(reviewedPath, ProfileSource.Reviewed), build, hash,
                0x2800000, CodeFixture());
            var automatic = CompatibilityContext.Load(new(automaticPath, ProfileSource.Automatic), build, hash,
                0x2800000, CodeFixture());
            if (reviewed.Global("dwLocalPlayerController") != 4096 ||
                reviewed.Field("C_BaseEntity", "m_iHealth") != 844 ||
                reviewed.ConVarInterfaceRva != 123456 || reviewed.CodeLayout.CameraEntryRva != 0x8827BC ||
                reviewed.CodeLayoutFingerprint.Length != 64)
                throw new Exception("Compatibility values were extracted incorrectly");
            reviewed.RequireEquivalent(automatic);
        }
        finally { Directory.Delete(root, true); }
    }),
    ("compatibility context rejects globals outside the client image", () =>
    {
        string root = TempDirectory(); const int build = 16001; string hash = new('6', 64);
        try
        {
            string negative = Path.Combine(root, "negative"), beyond = Path.Combine(root, "beyond");
            WriteDiscoveryProfile(negative, build, hash, globalOffset: -1);
            WriteDiscoveryProfile(beyond, build, hash, globalOffset: 0x2800000);
            Reject(() => CompatibilityContext.Load(new(negative, ProfileSource.Reviewed), build, hash,
                0x2800000, CodeFixture()));
            Reject(() => CompatibilityContext.Load(new(beyond, ProfileSource.Reviewed), build, hash,
                0x2800000, CodeFixture()));
        }
        finally { Directory.Delete(root, true); }
    }),
    ("compatibility identity rejects a changed client or code layout", () =>
    {
        string root = TempDirectory(); const int build = 16002; string hash = new('7', 64);
        try
        {
            string firstPath = Path.Combine(root, "first"), secondPath = Path.Combine(root, "second");
            WriteDiscoveryProfile(firstPath, build, hash);
            WriteDiscoveryProfile(secondPath, build, new string('8', 64));
            var first = CompatibilityContext.Load(new(firstPath, ProfileSource.Reviewed), build, hash,
                0x2800000, CodeFixture());
            var changedClient = CompatibilityContext.Load(new(secondPath, ProfileSource.Reviewed), build,
                new string('8', 64), 0x2800000, CodeFixture());
            Reject(() => first.RequireEquivalent(changedClient));
            var changedCode = CompatibilityContext.Load(new(firstPath, ProfileSource.Reviewed), build, hash,
                0x2800000, CodeFixture(compareCallLow: 0x30));
            Reject(() => first.RequireEquivalent(changedCode));
        }
        finally { Directory.Delete(root, true); }
    }),
    ("new recovery journals require matching client and code fingerprints", () =>
    {
        string client = new('9', 64), layout = new('a', 64);
        CompatibilityJournalPolicy.Require(14189, client, layout, 14189, client, layout, ProfileSource.Reviewed);
        CompatibilityJournalPolicy.Require(17000, client, layout, 17000, client, layout, ProfileSource.Automatic);
        Reject(() => CompatibilityJournalPolicy.Require(14189, new string('b', 64), layout,
            14189, client, layout, ProfileSource.Reviewed));
        Reject(() => CompatibilityJournalPolicy.Require(14189, client, new string('b', 64),
            14189, client, layout, ProfileSource.Reviewed));
        Reject(() => CompatibilityJournalPolicy.Require(14189, client, null,
            14189, client, layout, ProfileSource.Reviewed));
    }),
    ("legacy recovery journals require a reviewed static layout", () =>
    {
        string client = new('c', 64), layout = new('d', 64);
        CompatibilityJournalPolicy.Require(14186, null, null, 14186, client, layout, ProfileSource.Reviewed);
        CompatibilityJournalPolicy.Require(14189, null, null, 14189, client, layout, ProfileSource.Reviewed);
        Reject(() => CompatibilityJournalPolicy.Require(17000, null, null,
            17000, client, layout, ProfileSource.Automatic));
        Reject(() => CompatibilityJournalPolicy.Require(14189, null, null,
            14189, client, layout, ProfileSource.Automatic));
        Reject(() => CompatibilityJournalPolicy.Require(14188, null, null,
            14189, client, layout, ProfileSource.Reviewed));
    }),
    ("new ConVar build does not enable old player schema", () => Reject(() => Validation.Build(14185, 14186))),
    ("camera read failure preserves valid team switch state", () =>
    {
        var state = State();
        GameReader.Optional(state, () => throw new InvalidOperationException("Pawn changed during camera read"));
        Equal([2, 3], SwitchPolicy.Teams(state.Team ?? 0, state.FreezeTime == true, true, 75));
        if (state.Warnings.Count != 1) throw new Exception("Lost camera diagnostic failure");
    }),
    ("local pawn resolver rejects a recycled entity slot", () =>
    {
        ulong Pointer(ulong address) => address switch { 0x10010 => 0x20000, 0x20070 => 0x30000, 0x30010 => 0x20070, _ => throw new Exception("Unexpected read") };
        uint UInt(ulong address) => address == 0x20080 ? 0x10001U : 0U;
        Reject(() => LocalPawnResolver.Resolve(0x10000, 0x8001, Pointer, UInt, 16));
    }),
    ("local pawn resolver uses full handle and back pointer", () =>
    {
        ulong Pointer(ulong address) => address switch { 0x10010 => 0x20000, 0x20070 => 0x30000, 0x30010 => 0x20070, _ => throw new Exception("Unexpected read") };
        uint UInt(ulong address) => address == 0x20080 ? 0x8001U : 0U;
        if (LocalPawnResolver.Resolve(0x10000, 0x8001, Pointer, UInt, 16) != 0x30000) throw new Exception("Wrong pawn");
    }),
    ("null pointer rejected", () => Reject(() => Validation.Pointer(0))),
    ("kernel pointer rejected", () => Reject(() => Validation.Pointer(0xFFFF800000000000))),
    ("short read rejected", () => Reject(() => Validation.ReadLength(3, 4))),
    ("exact read accepted", () => Validation.ReadLength(4, 4)),
    ("NaN vector rejected", () => Reject(() => Validation.Vector([1, float.NaN, 3]))),
    ("finite vector accepted", () => Validation.Vector([1, 2, 3])),
    ("sequence performs round trip", () =>
    {
        var taps = new List<ushort>();
        SwitchSequence.Execute(() => State(), () => true, () => { }, taps.Add, (_, guard) => guard(), (_, _) => { }, 75);
        Equal([2, 3], taps.ToArray());
    }),
    ("sequence returns to the original team from a transient spectator state", () =>
    {
        int captures = 0;
        var taps = new List<ushort>();
        SwitchSequence.Execute(() => { var s = State(); if (++captures == 2) s.Team = 0; return s; },
            () => true, () => { }, taps.Add, (_, guard) => guard(), (_, _) => { }, 75);
        Equal([2, 3], taps.ToArray());
    }),
    ("fresh state blocks first input after freeze ends", () =>
    {
        int taps = 0;
        Reject(() => SwitchSequence.Execute(() => State(false), () => true, () => { }, _ => taps++, (_, guard) => guard(), (_, _) => { }, 75));
        if (taps != 0) throw new Exception("Sent input after freeze time");
    }),
    ("cancel during second capture blocks return", () =>
    {
        int captures = 0, taps = 0;
        bool cancelled = false;
        Cancelled(() => SwitchSequence.Execute(() => { cancelled = ++captures == 2; return State(); }, () => true,
            () => { if (cancelled) throw new OperationCanceledException(); }, _ => taps++, (_, guard) => guard(), (_, _) => { }, 75));
        if (taps != 1) throw new Exception("Return input sent after cancellation");
    }),
    ("focus loss during delay is latched", () =>
    {
        bool foreground = true;
        int taps = 0;
        Reject(() => SwitchSequence.Execute(() => State(), () => foreground, () => { }, _ => taps++,
            (_, guard) => { foreground = false; guard(); foreground = true; }, (_, _) => { }, 75));
        if (taps != 1) throw new Exception("Incorrect input count after focus loss");
    }),
    ("fullbright changes cheat flag only", () =>
    {
        if (ConVarPolicy.Unlock("mat_fullbright", 3, 0x400004000) != 0x400000000)
            throw new Exception("Incorrect fullbright flag mask");
    }),
    ("freeze time changes replicated flag only", () =>
    {
        if (ConVarPolicy.Unlock("spec_freeze_time", 7, 0x28200C) != 0x28000C)
            throw new Exception("Incorrect freeze flag mask");
    }),
    ("unknown cvar rejected", () => Reject(() => ConVarPolicy.Mask("sv_cheats", 0))),
    ("wrong fullbright type rejected", () => Reject(() => ConVarPolicy.Mask("mat_fullbright", 7))),
    ("restore preserves unrelated flag changes", () =>
    {
        if (ConVarPolicy.Restore("mat_fullbright", 3, 0x400004000, 0x400000080) != 0x400004080)
            throw new Exception("Unrelated flags overwritten");
    }),
    ("restore does not add a flag absent originally", () =>
    {
        if (ConVarPolicy.Restore("spec_freeze_time", 7, 0x28000C, 0x28200C) != 0x28000C)
            throw new Exception("Original restriction state not restored");
    }),
    ("registry reallocation rejects saved identity", () =>
    {
        var original = Entry("mat_fullbright", 3, 0x4000);
        Reject(() => ConVarRecovery.ValidateIdentity(original, original with { NodeAddress = 0x70000 }));
    }),
    ("restoration attempts both entries when one write fails", () =>
    {
        var entries = new[] { Entry("mat_fullbright", 3, 0x4000), Entry("spec_freeze_time", 7, 0x2000) };
        var values = entries.ToDictionary(e => e.Name, _ => 0UL);
        int writes = 0;
        var errors = ConVarRecovery.RestoreFlags(entries, e => values[e.Name], (e, value) =>
        {
            writes++;
            if (e.Name == "spec_freeze_time") throw new IOException("Injected write failure");
            values[e.Name] = value;
        });
        if (writes != 2 || errors.Length != 1 || values["mat_fullbright"] != 0x4000)
            throw new Exception("Rollback did not attempt both entries");
    }),
    ("restoration detects a write that did not persist", () =>
    {
        var errors = ConVarRecovery.RestoreFlags([Entry("mat_fullbright", 3, 0x4000)], _ => 0, (_, _) => { });
        if (errors.Length != 1) throw new Exception("Lost restoration failure");
    }),
    ("failed journal write leaves no authoritative journal", () =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "camera-probe-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "journal.json");
        try { JournalStorage.Publish(path, stream => { stream.WriteByte(123); throw new IOException("Injected disk failure"); }); }
        catch (IOException) { }
        if (File.Exists(path)) throw new Exception("Incomplete journal was published");
    }),
    ("journal publication refuses an existing journal", () =>
    {
        string directory = Path.Combine(Path.GetTempPath(), "camera-probe-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "journal.json");
        File.WriteAllText(path, "original");
        bool rejected = false;
        try { JournalStorage.Publish(path, stream => stream.WriteByte(123)); } catch (IOException) { rejected = true; }
        if (!rejected || File.ReadAllText(path) != "original") throw new Exception("Original journal overwritten");
    }),
    ("native flag write changes exactly eight bytes in the test process", () =>
    {
        nint memory = Marshal.AllocHGlobal(24);
        try
        {
            Marshal.Copy(Enumerable.Repeat((byte)0xA5, 24).ToArray(), 0, memory, 24);
            using var handle = Native.OpenProcess(0x20 | 0x08, false, Environment.ProcessId);
            if (handle.IsInvalid) throw new Exception("Cannot open test process");
            ConVarSession.WriteFlags(handle, (ulong)(memory + 8), 0x1234567812345678);
            byte[] result = new byte[24];
            Marshal.Copy(memory, result, 0, 24);
            if (result.Take(8).Concat(result.Skip(16)).Any(b => b != 0xA5) ||
                BitConverter.ToUInt64(result, 8) != 0x1234567812345678)
                throw new Exception("Write value or boundaries incorrect");
        }
        finally { Marshal.FreeHGlobal(memory); }
    }),
    ("aborted return retains between snapshot", () =>
    {
        int captures = 0, taps = 0;
        var kinds = new List<string>();
        Reject(() => SwitchSequence.Execute(() => { var s = State(); s.RoundStartCount = ++captures; return s; },
            () => true, () => { }, _ => taps++, (_, guard) => guard(), (kind, _) => kinds.Add(kind), 75));
        if (taps != 1 || !kinds.Contains("between")) throw new Exception("Lost abort evidence or sent wrong input");
    })
};
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} passed");
return failed == 0 ? 0 : 1;

static void Equal(ushort[] expected, ushort[] actual)
{
    if (!expected.SequenceEqual(actual)) throw new Exception("Incorrect key order");
}
static void Reject(Action action)
{
    try { action(); } catch (InvalidOperationException) { return; }
    throw new Exception("Expected rejection");
}
static Snapshot State(bool freeze = true) => new() { Team = 3, FreezeTime = freeze, RoundStartCount = 1 };
static ConVarEntry Entry(string name, short type, ulong flags) => new(name, type, flags, "0", 0x10000, 0x20000, 0x30000);
static void Cancelled(Action action)
{
    try { action(); } catch (OperationCanceledException) { return; }
    throw new Exception("Expected cancellation");
}

static (int Rva, byte[] Code)[] CodeFixture(bool duplicateMovement = false, bool outOfRangeCall = false,
    byte compareCallLow = 0x2F, bool decoyCameraEntry = false, bool duplicateCameraCandidate = false)
{
    const int cameraRva = 0x882700, movementRva = 0x8C4A00;
    byte[] camera = new byte[0x200], movement = new byte[0x200];
    Convert.FromHexString("488B4F38488B01FF90E804000084C0756D").CopyTo(camera, 0xBC);
    Convert.FromHexString("F30F108058140000").CopyTo(camera, 0xE7);
    byte[] compare = Convert.FromHexString("E82F9B8DFF0F2F05D4B2380176204D8BCF4D8BC6488BD6488BCFE8D5F2FFFF");
    compare[1] = compareCallLow;
    if (outOfRangeCall) BitConverter.GetBytes(int.MaxValue).CopyTo(compare, 1);
    compare.CopyTo(camera, 0x10C);
    byte[] health = Convert.FromHexString("4439B84C0300007F1E");
    health.CopyTo(movement, 0xDE);
    if (duplicateMovement) health.CopyTo(movement, 0x120);
    var sections = new List<(int Rva, byte[] Code)> { (cameraRva, camera), (movementRva, movement) };
    if (decoyCameraEntry)
    {
        byte[] decoy = new byte[0x40];
        Convert.FromHexString("488B4F38488B01FF90E804000084C0756D").CopyTo(decoy, 4);
        sections.Add((0x900000, decoy));
    }
    if (duplicateCameraCandidate) sections.Add((0x910000, camera.ToArray()));
    return sections.ToArray();
}

static string TempDirectory()
{
    string path = Path.Combine(Path.GetTempPath(), "camera-probe-test-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static void WriteDiscoveryProfile(string directory, int build, string? clientHash = null, int globalOffset = 4096,
    int conVarOffset = 3851888)
{
    Directory.CreateDirectory(directory);
    File.WriteAllText(Path.Combine(directory, "info.json"), $"{{\"build_number\":{build}}}");
    File.WriteAllText(Path.Combine(directory, "offsets.json"),
        $"{{\"client.dll\":{{\"dwLocalPlayerController\":{globalOffset},\"dwGameRules\":8192,\"dwGameEntitySystem\":12288,\"dwViewMatrix\":16384,\"dwViewAngles\":20480}}}}");
    File.WriteAllText(Path.Combine(directory, "interfaces.json"),
        $"{{\"tier0.dll\":{{\"VEngineCvar007\":{conVarOffset}}}}}");
    var fields = new Dictionary<string, Dictionary<string, int>>
    {
        ["CEntityInstance"] = new() { ["m_pEntity"] = 16 },
        ["CEntityIdentity"] = new() { ["m_designerName"] = 32 },
        ["CBasePlayerController"] = new() { ["m_bIsLocalPlayerController"] = 1712, ["m_hPawn"] = 1716 },
        ["CCSPlayerController"] = new() { ["m_hPlayerPawn"] = 2044, ["m_hObserverPawn"] = 2052, ["m_bPawnIsAlive"] = 2072 },
        ["C_BaseEntity"] = new() { ["m_iTeamNum"] = 1003, ["m_pGameSceneNode"] = 816, ["m_fFlags"] = 916,
            ["m_hGroundEntity"] = 1012, ["m_MoveType"] = 535, ["m_vecAbsVelocity"] = 976,
            ["m_vecServerVelocity"] = 1016, ["m_iHealth"] = 844, ["m_lifeState"] = 848 },
        ["CGameSceneNode"] = new() { ["m_vecAbsOrigin"] = 208 },
        ["C_BasePlayerPawn"] = new() { ["m_pMovementServices"] = 4912, ["m_flDeathTime"] = 5208,
            ["m_hController"] = 5132, ["v_angle"] = 5392, ["m_vecLastCameraSetupLocalOrigin"] = 5504,
            ["m_flLastCameraSetupTime"] = 5516, ["m_pCameraServices"] = 4928, ["m_pObserverServices"] = 4944 },
        ["CPlayer_MovementServices"] = new() { ["m_nButtons"] = 80, ["m_nLastCommandNumberProcessed"] = 140,
            ["m_flCmdForwardMove"] = 156 },
        ["CCSPlayer_MovementServices"] = new() { ["m_ModernJump"] = 1736, ["m_nLastJumpTick"] = 1728 },
        ["CCSPlayerModernJump"] = new() { ["m_nLastActualJumpPressTick"] = 24,
            ["m_nLastUsableJumpPressTick"] = 28, ["m_nLastLandedTick"] = 32 },
        ["C_CSGameRules"] = new() { ["m_bFreezePeriod"] = 56, ["m_nRoundStartCount"] = 132 },
        ["CPlayer_CameraServices"] = new() { ["m_hViewEntity"] = 80 },
        ["CPlayer_ObserverServices"] = new() { ["m_iObserverMode"] = 64, ["m_hObserverTarget"] = 68,
            ["m_bForcedObserverMode"] = 76, ["m_iObserverLastMode"] = 80 }
    };
    var classes = fields.ToDictionary(pair => pair.Key, pair => new { fields = pair.Value });
    File.WriteAllText(Path.Combine(directory, "client_dll.json"),
        System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["client.dll"] = new { classes }
        }));
    if (clientHash is not null)
        File.WriteAllText(Path.Combine(directory, "provenance.json"),
            $"{{\"kind\":\"automatic-local-discovery\",\"engine_build\":{build},\"client_sha256\":\"{clientHash}\"}}");
}
