"""Tooling regressions with real Git/PowerShell and mocked external work.

Run tests/tooling/run.sh on the Docker host with the prepared tooling image.
No .NET build, test campaign, network access, or nested Docker is required.
"""

import fcntl
import importlib.util
import json
import os
from pathlib import Path
import selectors
import shutil
import signal
import subprocess
import sys
import tempfile
import textwrap
import time
import unittest


REPOSITORY = Path(__file__).resolve().parents[2]
GIT = shutil.which("git")

spec = importlib.util.spec_from_file_location("release_trust", REPOSITORY / "eng/scripts/Check-ReleaseTrust.py")
release_trust = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release_trust)


class ReleaseTrustTests(unittest.TestCase):
    def setUp(self):
        self.sha = "a" * 40
        self.workflow = {"id": 7, "path": ".github/workflows/ci.yml", "state": "active"}
        self.run = {"id": 10, "workflow_id": 7, "path": self.workflow["path"],
                    "head_sha": self.sha, "head_branch": "master", "event": "push",
                    "repository": {"id": 42}, "head_repository": {"id": 42},
                    "status": "completed", "conclusion": "success"}

    def test_accepts_exact_canonical_success(self):
        release_trust.validate_run(self.run, self.workflow, self.sha, 42)

    def test_rejects_forged_pending_failed_and_wrong_commit_runs(self):
        for change in ({"head_sha": "b" * 40}, {"workflow_id": 8},
                       {"path": ".github/workflows/impostor.yml"}, {"head_branch": "Master"},
                       {"event": "pull_request"}, {"repository": {"id": 43}},
                       {"head_repository": {"id": 43}}, {"status": "in_progress"},
                       {"conclusion": "failure"}, {"conclusion": "cancelled"}):
            with self.subTest(change=change), self.assertRaises(ValueError):
                release_trust.validate_run(dict(self.run, **change), self.workflow, self.sha, 42)

    def test_dispatch_and_forks_cannot_authorize_publication(self):
        for repository, event in (("other/SharpProof", "push"),
                                  (release_trust.REPOSITORY, "workflow_dispatch")):
            with self.subTest(event=event), self.assertRaises(ValueError):
                release_trust.check(self.sha, "refs/tags/v1.0.0", repository, event,
                                    lambda route: self.fail("Must reject before API access"))

    def test_latest_attempt_must_pass_and_commit_must_be_on_master(self):
        def fetch(route):
            if route == "":
                return {"full_name": release_trust.REPOSITORY, "default_branch": "master", "id": 42}
            if route.startswith("compare/"):
                return {"status": "ahead", "merge_base_commit": {"sha": self.sha}}
            if "/runs?" in route:
                return {"workflow_runs": [dict(self.run, id=9), self.run]}
            if route == "actions/runs/10":
                return dict(self.run, conclusion="failure")
            return self.workflow
        with self.assertRaisesRegex(ValueError, "Latest CI attempt"):
            release_trust.check(self.sha, "refs/tags/v1.0.0", release_trust.REPOSITORY, "push", fetch)
        def diverged(route):
            return {"status": "diverged"} if route.startswith("compare/") else fetch(route)
        with self.assertRaisesRegex(ValueError, "not an ancestor"):
            release_trust.check(self.sha, "refs/tags/v1.0.0", release_trust.REPOSITORY, "push", diverged)


class ToolingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="sharpproof-tooling-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.repo = self.root / "repo"
        self.bin = self.root / "bin"
        self.repo.mkdir()
        self.bin.mkdir()
        self.log = self.root / "docker.jsonl"
        self.env = os.environ.copy()
        for key in ("SHARPPROOF_TOOLING_IMAGE", "SHARPPROOF_WINDOWS_NATIVE_CACHE", "GITHUB_STEP_SUMMARY"):
            self.env.pop(key, None)
        # PowerShell initializes profile/cache directories before loading a script.
        # Keep those writes private to each fixture on the container's /tmp mount.
        for key in ("HOME", "XDG_CACHE_HOME", "XDG_CONFIG_HOME", "XDG_DATA_HOME", "XDG_STATE_HOME", "XDG_RUNTIME_DIR"):
            directory = self.root / key.lower()
            directory.mkdir(mode=0o700)
            self.env[key] = str(directory)
        self.env.update(PATH=str(self.bin) + os.pathsep + self.env["PATH"],
                        COMPOSE_PROJECT_NAME="tooling-regression", MOCK_DOCKER_LOG=str(self.log),
                        LONGFUZZ_MINUTES="1", LONGFUZZ_SEED_BASE="1")
        self.copy("ci.sh")
        self.write("tests/tooling/run.sh", 'docker mocked-tooling-regressions "$@"\n')
        self.write("eng/scripts/Test-SharpProofConsumerBaseline.sh", 'docker mocked-consumer-baseline "$@"\n')
        self.command("docker", r'''
            import json, os, pathlib, sys, time
            args = sys.argv[1:]
            mounts = [args[i + 1] for i, value in enumerate(args) if value == "--volume"]
            pointers = {}
            for mount in mounts:
                if mount.endswith(":/workspace/SharpProof/.git:ro"):
                    path = mount.rsplit(":", 2)[0]
                    pointers[path] = pathlib.Path(path).read_text()
            with open(os.environ["MOCK_DOCKER_LOG"], "a") as log:
                log.write(json.dumps({"args": args, "pointers": pointers}) + "\n")
            if args[:3] == ["buildx", "imagetools", "inspect"]:
                print("sha256:" + "a" * 64)
            if "SHARPPROOF_LONGFUZZ_OUTPUT=/workspace/LongFuzzOutput" in args:
                mount = next(m for m in mounts if m.endswith(":/workspace/LongFuzzOutput"))
                output = pathlib.Path(mount.rsplit(":", 1)[0])
                (output / "events.log").write_text("::notice::current fuzz progress\n")
                (output / "stats-mock.json").write_text(json.dumps({"Harness": "mock", "Cases": 1,
                    "SeedStart": 1, "LastSeed": 1, "ElapsedMinutes": 1, "Hits": 0}))
                if os.environ.get("MOCK_FUZZ_HIT") == "1":
                    (output / "hits.jsonl").write_text(json.dumps({"Severity": "error"}) + "\n")
                if "MOCK_FUZZ_RELEASE" in os.environ:
                    deadline = time.monotonic() + 20
                    while not pathlib.Path(os.environ["MOCK_FUZZ_RELEASE"]).exists():
                        if time.monotonic() > deadline:
                            sys.exit(99)
                        time.sleep(0.02)
        ''')
        self.command("jq", r'''
            import json, sys
            records = [json.loads(line) for line in open(sys.argv[-1]) if line.strip()]
            if "-s" in sys.argv:
                print(sum(r.get("Severity") == "error" for r in records))
            elif "-r" in sys.argv:
                print("| mock | 1 | 1..1 | 1 | 0 |")
            else:
                print("mock finding")
        ''')

    def write(self, relative, content):
        path = self.repo / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(textwrap.dedent(content), encoding="utf-8")
        return path

    def copy(self, relative):
        return self.write(relative, (REPOSITORY / relative).read_text(encoding="utf-8"))

    def command(self, name, body):
        path = self.bin / name
        path.write_text("#!" + sys.executable + "\n" + textwrap.dedent(body), encoding="utf-8")
        path.chmod(0o755)

    def git(self, *args):
        return subprocess.run([GIT, "-C", str(self.repo), *args], env=self.env,
                              check=True, capture_output=True, text=True, timeout=15)

    def initialize_git(self):
        self.git("init", "-q")
        self.git("config", "user.email", "tooling-test@example.invalid")
        self.git("config", "user.name", "Tooling regression")
        self.git("add", ".")
        self.git("commit", "-qm", "fixture")

    def ci(self, *lanes, env=None):
        return subprocess.run(["bash", str(self.repo / "ci.sh"), *lanes],
                              env=env or self.env, capture_output=True, text=True, timeout=30)

    def calls(self):
        return [json.loads(line) for line in self.log.read_text().splitlines()]

    def evidence(self):
        self.write("artifacts/api-coverage/catalog-test-evidence.json", "{}")
        self.write("artifacts/api-coverage/intrinsic-test-evidence.json", "{}")

    def test_image_override_reaches_build_lint_and_scan(self):
        self.initialize_git()
        self.evidence()
        self.env["SHARPPROOF_TOOLING_IMAGE"] = "example.invalid/custom:tested"
        result = self.ci("image", "repository")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        calls = [call["args"] for call in self.calls()]
        self.assertTrue(any("--tag" in a and a[a.index("--tag") + 1] == self.env["SHARPPROOF_TOOLING_IMAGE"] for a in calls))
        self.assertTrue(any("canonical_tooling=docker-image://example.invalid/custom:tested" in a for a in calls))
        self.assertTrue(any("docker:example.invalid/custom:tested" in a for a in calls))
        self.assertIn(["mocked-tooling-regressions", "-v"], calls)
        self.assertIn(["mocked-consumer-baseline"], calls)

    def test_default_image_and_ordinary_checkout(self):
        self.initialize_git()
        result = self.ci("image", "build")
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("tooling-regression-tooling:local", self.calls()[0]["args"])
        self.assertFalse(self.calls()[1]["pointers"])

    def test_tooling_cache_requires_verified_provenance_and_patched_cli(self):
        self.initialize_git()
        self.env["SHARPPROOF_IMAGE_REGISTRY"] = "ghcr.io/alexyorke/sharpproof-tooling"
        self.command("gh", '''
            import os, sys
            if sys.argv[1] == "--version":
                print("gh version " + os.environ["MOCK_GH_VERSION"])
            else:
                sys.exit(int(os.environ["MOCK_ATTESTATION_STATUS"]))
        ''')
        for version, status, trusted in (("2.101.0", "0", False),
                                         ("2.104.0", "1", False),
                                         ("2.104.0", "0", True)):
            with self.subTest(version=version, status=status):
                self.log.unlink(missing_ok=True)
                env = dict(self.env, MOCK_GH_VERSION=version, MOCK_ATTESTATION_STATUS=status)
                result = self.ci("image", env=env)
                self.assertEqual(0, result.returncode, result.stdout + result.stderr)
                calls = [call["args"] for call in self.calls()]
                pulls = [a for a in calls if a[0] == "pull"]
                self.assertEqual(trusted, bool(pulls))
                self.assertEqual(not trusted, any(a[:2] == ["buildx", "build"] for a in calls))
                if trusted:
                    self.assertEqual(self.env["SHARPPROOF_IMAGE_REGISTRY"] + "@sha256:" + "a" * 64,
                                     pulls[0][-1])

    def test_linked_worktree_mounts_metadata_for_all_compose_lanes(self):
        self.initialize_git()
        common = self.repo / ".git"
        linked = self.root / "linked"
        self.git("worktree", "add", "--detach", str(linked), "HEAD")
        self.repo = linked
        self.evidence()
        result = self.ci("build", "repository", "evidence", "fuzz-0")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        calls = [call for call in self.calls() if call["args"][:1] == ["compose"] and "run" in call["args"]]
        # The lint run reuses the lint image built from the pulled tooling image.
        lint_runs = [call["args"] for call in calls if call["args"][-1] == "lint"]
        self.assertTrue(lint_runs)
        for args in lint_runs:
            self.assertEqual(["compose", "-f", "compose.yaml", "-f", "eng/ci/compose.prebuilt-lint.yaml", "run"], args[:6])
        commands = [call["args"][call["args"].index("tooling") + 1:]
                    if "tooling" in call["args"] else [call["args"][-1]] for call in calls]
        self.assertEqual({"security", "format-check", "lint", "docs", "layout", "test", "coverage"},
                         {command[0] for command in commands})
        self.assertIn(["coverage", "-CoveragePart", "Isolated"], commands)
        self.assertEqual({"FullyQualifiedName~DotnetApiInventoryTests|FullyQualifiedName~ApiSpecRuntimeOracleTests", "TestCategory=LongFuzz"},
                         {command[command.index("-TestFilter") + 1] for command in commands if "-TestFilter" in command})
        for call in calls:
            self.assertIn(str(common) + ":/git-common:ro", call["args"])
            self.assertEqual(1, len(call["pointers"]))
            pointer, contents = next(iter(call["pointers"].items()))
            self.assertEqual("gitdir: /git-common/worktrees/linked\n", contents)
            self.assertFalse(Path(pointer).exists(), "temporary pointer must be removed")

    def test_fuzz_runs_do_not_reuse_prior_findings(self):
        self.initialize_git()
        stale = self.write("artifacts/long-fuzz/hits.jsonl", '{"Severity":"error"}\n')
        first = self.ci("fuzz-0", env=dict(self.env, MOCK_FUZZ_HIT="1"))
        self.assertNotEqual(0, first.returncode)
        second = self.ci("fuzz-0")
        self.assertEqual(0, second.returncode, second.stdout + second.stderr)
        self.assertIn("0 error finding(s)", second.stdout)
        self.assertTrue(stale.exists(), "previous evidence remains available")
        mounts = [a for call in self.calls() for a in call["args"] if a.endswith(":/workspace/LongFuzzOutput")]
        self.assertEqual(2, len(set(mounts)))

    def test_fuzz_progress_is_visible_before_the_producer_exits(self):
        self.initialize_git()
        release = self.root / "release"
        process = subprocess.Popen(["bash", str(self.repo / "ci.sh"), "fuzz-0"],
                                   env=dict(self.env, MOCK_FUZZ_RELEASE=str(release)),
                                   stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        output = b""
        try:
            with selectors.DefaultSelector() as selector:
                selector.register(process.stdout, selectors.EVENT_READ)
                deadline = time.monotonic() + 10
                while b"::notice::current fuzz progress" not in output and time.monotonic() < deadline:
                    for key, _ in selector.select(0.1):
                        output += os.read(key.fd, 4096)
            self.assertIn(b"::notice::current fuzz progress", output)
            self.assertIsNone(process.poll(), "progress must precede command completion")
        finally:
            release.touch()
            remainder, _ = process.communicate(timeout=25)
            output += remainder
        self.assertEqual(0, process.returncode, output.decode())

    def loop_environment(self):
        workspace = tempfile.TemporaryDirectory(prefix="loop-", dir=os.environ["SHARPPROOF_TOOLING_TEST_WORKSPACE"])
        self.addCleanup(workspace.cleanup)
        target = Path(workspace.name) / "target"
        target.mkdir()
        source = self.root / "source"
        artifacts = self.root / "artifacts"
        source.mkdir()
        artifacts.mkdir()
        env = dict(self.env, SHARPPROOF_LOOP_SOURCE_ROOT=str(source), SHARPPROOF_REPO_ROOT=str(target),
                   SHARPPROOF_LOOP_ARTIFACTS_ROOT=str(artifacts), SHARPPROOF_ORIGIN_URL="https://example.invalid/repo.git")
        return target, env

    def test_loop_lock_is_attached_to_the_shared_workspace(self):
        target, env = self.loop_environment()
        descriptor = os.open(target, os.O_RDONLY)
        try:
            fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
            result = subprocess.run(["bash", str(REPOSITORY / "eng/container/loop-command.sh"), "layout"],
                                    env=env, capture_output=True, text=True, timeout=10)
            self.assertEqual(125, result.returncode, result.stderr)
            self.assertIn("Another SharpProof loop command owns", result.stderr)
        finally:
            os.close(descriptor)

    def test_loop_signal_exits_and_releases_the_workspace_lock(self):
        target, env = self.loop_environment()
        entered, release = self.root / "entered", self.root / "release"
        env.update(MOCK_LOOP_ENTERED=str(entered), MOCK_LOOP_RELEASE=str(release))
        self.command("git", r'''
            import os, pathlib, sys, time
            if "--get-all" in sys.argv:
                pathlib.Path(os.environ["MOCK_LOOP_ENTERED"]).touch()
                deadline = time.monotonic() + 20
                while not pathlib.Path(os.environ["MOCK_LOOP_RELEASE"]).exists():
                    if time.monotonic() > deadline:
                        sys.exit(99)
                    time.sleep(0.02)
                sys.exit(1)
            if "--is-inside-work-tree" in sys.argv:
                sys.exit(1)
        ''')
        process = subprocess.Popen(["bash", str(REPOSITORY / "eng/container/loop-command.sh"), "layout"],
                                   env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        descriptor = os.open(target, os.O_RDONLY)
        try:
            deadline = time.monotonic() + 10
            while not entered.exists() and process.poll() is None and time.monotonic() < deadline:
                time.sleep(0.02)
            self.assertTrue(entered.exists(), "loop must reach the locked section")
            with self.assertRaises(BlockingIOError):
                fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
            process.send_signal(signal.SIGTERM)
        finally:
            release.touch()
            stdout, stderr = process.communicate(timeout=25)
            os.close(descriptor)
        self.assertEqual(143, process.returncode, (stdout + stderr).decode())
        descriptor = os.open(target, os.O_RDONLY)
        try:
            fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
        finally:
            os.close(descriptor)

    def powershell_fixture(self):
        self.write("eng/scripts/SharpProof.ContainerExecution.psm1", '''
            function Assert-SharpProofContainer { param($Message) }
            function Assert-SharpProofTestSwitches { param([switch]$Fast, [switch]$NoBuild) }
            function Resolve-SharpProofSolutionTestTimeoutSeconds { param($RepositoryRoot, $TimeoutSeconds, $WasSpecified) return 60 }
            function Get-SharpProofSemanticTestParallelism { param($RepositoryRoot) return 1 }
            function Get-SharpProofDotnetWrapperPath { return '/unused' }
        ''')

    def pwsh(self, script, *arguments):
        return subprocess.run(["pwsh", "-NoLogo", "-NoProfile", "-File", str(self.repo / "eng/scripts" / script), *arguments],
                              env=self.env, capture_output=True, text=True, timeout=30)

    def test_package_coverage_forwards_the_requested_filter(self):
        self.powershell_fixture()
        self.copy("eng/scripts/Invoke-SharpProofCoverage.ps1")
        self.copy("eng/scripts/Resolve-SharpProofContainedPath.ps1")
        self.write("eng/scripts/Invoke-SharpProofPackageTests.ps1", '''
            param($Configuration, [switch]$Fast, [switch]$NoBuild, $TimeoutSeconds, $TestFilter,
                $PackagePartition, $PackagePartitionCount, $CoverageSettings, $CoverageResultsDirectory)
            $PSBoundParameters | ConvertTo-Json | Set-Content -LiteralPath $env:MOCK_PACKAGE_ARGUMENTS
            [void][IO.Directory]::CreateDirectory($CoverageResultsDirectory)
            $root = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
            '<coverage><sources><source>' + $root + '</source></sources><packages><package><classes>' +
                '<class filename="src/Subject.cs"><lines><line number="1" hits="1"/></lines></class>' +
                '</classes></package></packages></coverage>' |
                Set-Content (Join-Path $CoverageResultsDirectory 'mock.cobertura.xml')
            $global:LASTEXITCODE = 0
        ''')
        arguments = self.root / "package.json"
        self.env["MOCK_PACKAGE_ARGUMENTS"] = str(arguments)
        result = self.pwsh("Invoke-SharpProofCoverage.ps1", "-Part", "Package", "-TestFilter", "FullyQualifiedName~ChosenFixture")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertEqual("FullyQualifiedName~ChosenFixture", json.loads(arguments.read_text())["TestFilter"])

    def changed_fixture(self):
        self.powershell_fixture()
        self.copy("eng/scripts/Invoke-SharpProofChangedTests.ps1")
        self.write("src/ConsoleLoggingAnalyzer/ConsoleLoggingAnalyzer.csproj", '<Project Sdk="Microsoft.NET.Sdk" />')
        self.write("src/ConsoleLoggingAnalyzer/Analyzer.cs", "class Analyzer {}")
        self.write("src/ConsoleLoggingAnalyzer.Tests/ConsoleLoggingAnalyzer.Tests.csproj", '''
            <Project Sdk="Microsoft.NET.Sdk"><ItemGroup>
              <ProjectReference Include="../ConsoleLoggingAnalyzer/ConsoleLoggingAnalyzer.csproj" />
            </ItemGroup></Project>
        ''')
        self.write("src/ConsoleLoggingAnalyzer.Tests/Tests.cs", "class Tests {}")
        self.write("tests/Other.UnitTests/Other.UnitTests.csproj", '<Project Sdk="Microsoft.NET.Sdk" />')
        self.initialize_git()

    def changed_plan(self):
        result = self.pwsh("Invoke-SharpProofChangedTests.ps1", "-PlanOnly", "-ComparisonRef", "HEAD")
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("ConsoleLoggingAnalyzer.Tests.csproj", result.stdout)
        return result.stdout

    def test_changed_analyzer_selects_its_non_sharpproof_named_test_project(self):
        self.changed_fixture()
        self.write("src/ConsoleLoggingAnalyzer/Analyzer.cs", "class Analyzer { int changed; }")
        self.assertNotIn("Other.UnitTests.csproj", self.changed_plan())

    def test_changed_test_source_selects_its_non_sharpproof_named_test_project(self):
        self.changed_fixture()
        self.write("src/ConsoleLoggingAnalyzer.Tests/Tests.cs", "class Tests { int changed; }")
        self.assertNotIn("Other.UnitTests.csproj", self.changed_plan())

    def test_changed_fallback_includes_all_test_filename_suffixes(self):
        self.changed_fixture()
        self.write("unowned.txt", "changed")
        self.assertIn("Other.UnitTests.csproj", self.changed_plan())


if __name__ == "__main__":
    unittest.main()
