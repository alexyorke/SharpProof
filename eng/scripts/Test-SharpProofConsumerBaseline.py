#!/usr/bin/env python3
"""Offline consumer baseline provenance, archive and initialization regressions."""

import copy
import hashlib
import io
import json
import os
from pathlib import Path
import stat
import tempfile
import textwrap
import unittest
from unittest import mock
import zipfile

import SharpProofConsumerBaseline as baseline


class FakeGitHub:
    def __init__(self, responses):
        self.responses = responses
        self.calls = []

    def json(self, route, query=None):
        self.calls.append((route, query))
        if route not in self.responses:
            raise AssertionError("Unexpected API request: " + route)
        return copy.deepcopy(self.responses[route])


class ConsumerBaselineTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="sharpproof-baseline-")
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.repository = "consumer/Example"
        self.workflow = ".github/workflows/sharpproof.yml"
        self.revision = "c" * 40
        self.image = "ghcr.io/alexyorke/sharpproof-preview@sha256:" + "d" * 64
        self.plan = dict(repository=self.repository, repositoryId=12, workflowPath=self.workflow,
                         workflowId=23, workflowRevision=self.revision, consumerImage=self.image,
                         project="src/app/Example.csproj", composeFile="compose.yaml", service="verify",
                         evidenceDirectory="artifacts/sharpproof", runId=200, runAttempt=1,
                         sourceSha="b" * 40, sourceBranch="feature", baseSha="a" * 40,
                         baseBranch="main", event="pull_request", initialize=False)
        self.run = dict(id=100, run_attempt=2, run_number=99, workflow_id=23, path=self.workflow,
                        repository=dict(id=12, full_name=self.repository),
                        head_repository=dict(id=12, full_name=self.repository),
                        head_sha="a" * 40, head_branch="main", event="push",
                        status="completed", conclusion="success", referenced_workflows=[
                            dict(path=baseline.PROVIDER + "@" + self.revision, sha=self.revision)])
        self.name = baseline.artifact_name(self.plan["project"], self.workflow, 100, 2)
        self.artifact = dict(id=300, name=self.name, expired=False, digest="sha256:" + "e" * 64,
                             workflow_run=dict(id=100, repository_id=12, head_repository_id=12,
                                               head_sha="a" * 40, head_branch="main"))

    def api(self):
        return FakeGitHub({f"repos/{self.repository}/actions/workflows/23/runs":
                           dict(total_count=1, workflow_runs=[self.run]),
                           f"repos/{self.repository}/actions/runs/100": self.run,
                           f"repos/{self.repository}/actions/runs/100/artifacts":
                           dict(total_count=1, artifacts=[self.artifact]),
                           f"repos/{self.repository}/actions/artifacts/300": self.artifact})

    def contents(self):
        source = self.root / "source"
        source.mkdir()
        for name in baseline.REQUIRED:
            (source / name).write_text('{"fixture":true}\n', encoding="utf-8")
        recorded = dict(self.plan, runId=100, runAttempt=2, sourceSha="a" * 40,
                        sourceBranch="main", event="push")
        destination = self.root / "recorded"
        baseline.record(recorded, source, destination)
        return {path.name: path.read_bytes() for path in destination.iterdir()}

    def archive(self, contents):
        output = io.BytesIO()
        with zipfile.ZipFile(output, "w") as zipped:
            for name, data in contents.items():
                zipped.writestr(name, data)
        archive = output.getvalue()
        return archive, dict(self.artifact, digest="sha256:" + hashlib.sha256(archive).hexdigest())

    def test_selects_numeric_artifact_for_exact_base_run_and_attempt(self):
        api = self.api()
        run, artifact = baseline.select_baseline(api, self.plan)
        self.assertEqual((100, 2, 300), (run["id"], run["run_attempt"], artifact["id"]))
        query = api.calls[0][1]
        self.assertEqual("a" * 40, query["head_sha"])
        self.assertEqual("main", query["branch"])
        self.assertIn((f"repos/{self.repository}/actions/artifacts/300", None), api.calls)

    def test_missing_baseline_never_uses_current_evidence(self):
        api = self.api()
        api.responses[f"repos/{self.repository}/actions/workflows/23/runs"] = dict(total_count=0, workflow_runs=[])
        with self.assertRaisesRegex(baseline.BaselineError, "current evidence is never a fallback"):
            baseline.select_baseline(api, self.plan)

    def test_pull_request_runs_are_not_trusted_base_runs(self):
        self.run["event"] = "pull_request"
        with self.assertRaisesRegex(baseline.BaselineError, "No trusted successful run"):
            baseline.select_baseline(self.api(), self.plan)

    def test_accepts_rest_workflow_paths_with_matching_branch_and_sha_refs(self):
        for suffix in ("", "@main", "@refs/heads/main", "@" + "a" * 40):
            with self.subTest(suffix=suffix):
                baseline.validate_run(dict(self.run, path=self.workflow + suffix), self.plan)
        for path in (".github/workflows/other.yml@main", self.workflow + "@other", self.workflow + "@"):
            with self.subTest(path=path), self.assertRaises(baseline.BaselineError):
                baseline.validate_run(dict(self.run, path=path), self.plan)

    def test_current_pr_workflow_path_may_use_its_exact_merge_ref(self):
        plan = dict(self.plan, pullRequestNumber=17)
        run = dict(self.run, id=200, run_attempt=1, event="pull_request", status="in_progress",
                   conclusion=None, head_sha="b" * 40, head_branch="feature",
                   path=self.workflow + "@refs/pull/17/merge")
        baseline.validate_run(run, plan, current=True)
        with self.assertRaises(baseline.BaselineError):
            baseline.validate_run(dict(run, path=self.workflow + "@refs/pull/18/merge"), plan, current=True)
    def test_rejects_wrong_run_repository_workflow_event_sha_and_pin(self):
        changes = [dict(head_sha="f" * 40), dict(head_branch="other"), dict(event="pull_request_target"),
                   dict(conclusion="failure"), dict(status="in_progress"), dict(workflow_id=24),
                   dict(path=".github/workflows/lookalike.yml"), dict(referenced_workflows=[]),
                   dict(head_repository=dict(id=55, full_name="fork/Example")),
                   dict(repository=dict(id=13, full_name=self.repository))]
        for change in changes:
            with self.subTest(change=change), self.assertRaises(baseline.BaselineError):
                baseline.validate_run(dict(self.run, **change), self.plan)

    def test_rejects_artifact_expiry_repository_sha_attempt_and_missing_digest(self):
        changes = [dict(expired=True), dict(name="lookalike"), dict(digest=None),
                   dict(workflow_run=dict(self.artifact["workflow_run"], repository_id=13)),
                   dict(workflow_run=dict(self.artifact["workflow_run"], head_repository_id=99)),
                   dict(workflow_run=dict(self.artifact["workflow_run"], head_sha="f" * 40)),
                   dict(workflow_run=dict(self.artifact["workflow_run"], id=101))]
        for change in changes:
            api = self.api()
            api.responses[f"repos/{self.repository}/actions/artifacts/300"] = dict(self.artifact, **change)
            with self.subTest(change=change), self.assertRaises(baseline.BaselineError):
                baseline.select_baseline(api, self.plan)

    def test_rejects_ambiguous_and_missing_artifact(self):
        for artifacts in ([], [self.artifact, self.artifact]):
            api = self.api()
            api.responses[f"repos/{self.repository}/actions/runs/100/artifacts"]["artifacts"] = artifacts
            with self.subTest(count=len(artifacts)), self.assertRaises(baseline.BaselineError):
                baseline.select_baseline(api, self.plan)

    def test_record_and_archive_round_trip_preserve_all_evidence_bytes(self):
        contents = self.contents()
        archive, artifact = self.archive(contents)
        destination = self.root / "validated"
        baseline.unpack(archive, artifact, self.plan, self.run, destination)
        self.assertEqual(contents, {path.name: path.read_bytes() for path in destination.iterdir()})

    def test_rejects_archive_tampering_even_when_evidence_files_look_valid(self):
        archive, artifact = self.archive(self.contents())
        with self.assertRaisesRegex(baseline.BaselineError, "archive digest differs"):
            baseline.unpack(archive + b"tampered", artifact, self.plan, self.run, self.root / "bad")

    def test_rejects_file_tampering_with_a_fresh_archive_hash(self):
        contents = self.contents()
        contents["result.json"] = b'{"tampered":true}'
        archive, artifact = self.archive(contents)
        with self.assertRaisesRegex(baseline.BaselineError, "file digest or size differs"):
            baseline.unpack(archive, artifact, self.plan, self.run, self.root / "bad")

    def test_rejects_incomplete_evidence_and_unexpected_members(self):
        original = self.contents()
        for removed in baseline.REQUIRED | {baseline.RECEIPT}:
            contents = dict(original)
            del contents[removed]
            with self.subTest(removed=removed), self.assertRaises(baseline.BaselineError):
                baseline.validate_receipt(contents, self.plan, self.run)
        archive, artifact = self.archive(dict(original, **{"../escape.json": b"bad"}))
        with self.assertRaisesRegex(baseline.BaselineError, "unsafe or oversized"):
            baseline.unpack(archive, artifact, self.plan, self.run, self.root / "bad")

    def test_rejects_archive_symlinks(self):
        output = io.BytesIO()
        with zipfile.ZipFile(output, "w") as zipped:
            item = zipfile.ZipInfo("result.json")
            item.external_attr = (stat.S_IFLNK | 0o777) << 16
            zipped.writestr(item, "request.json")
        archive = output.getvalue()
        artifact = dict(self.artifact, digest="sha256:" + hashlib.sha256(archive).hexdigest())
        with self.assertRaisesRegex(baseline.BaselineError, "unsafe or oversized"):
            baseline.unpack(archive, artifact, self.plan, self.run, self.root / "bad")

    def test_rejects_incompatible_or_untrusted_receipts(self):
        original = self.contents()
        receipt = json.loads(original[baseline.RECEIPT])
        changes = [dict(project="other.csproj"), dict(consumerImage=self.image.replace("d", "e")),
                   dict(workflowRevision="f" * 40), dict(runId=999), dict(runAttempt=1),
                   dict(sourceSha="f" * 40), dict(kind="comparison"), dict(files=[]), dict(schemaVersion=True)]
        for change in changes:
            contents = dict(original)
            contents[baseline.RECEIPT] = json.dumps(dict(receipt, **change)).encode()
            with self.subTest(change=change), self.assertRaises(baseline.BaselineError):
                baseline.validate_receipt(contents, self.plan, self.run)

    def test_rejects_duplicate_json_and_unsafe_relative_paths(self):
        with self.assertRaisesRegex(baseline.BaselineError, "Duplicate JSON"):
            baseline.decode_json('{"schemaVersion":1,"schemaVersion":1}')
        for path in ("", "../escape", "a/../escape", "/absolute", "a\\b", "./a", "a//b", "-argument", "a\tb"):
            with self.subTest(path=path), self.assertRaises(baseline.BaselineError):
                baseline.relative_path(path)

    def test_clears_only_dedicated_ordinary_evidence_output(self):
        evidence = self.root / "artifacts" / "sharpproof"
        evidence.mkdir(parents=True)
        (evidence / "result.json").write_text("stale", encoding="utf-8")
        keep = self.root / "artifacts" / "keep.txt"
        keep.write_text("keep", encoding="utf-8")
        baseline.clear_evidence(self.root, "artifacts/sharpproof")
        self.assertFalse(evidence.exists())
        self.assertEqual("keep", keep.read_text(encoding="utf-8"))
        for path in ("artifacts", "src/app", "../escape"):
            with self.subTest(path=path), self.assertRaises(baseline.BaselineError):
                baseline.clear_evidence(self.root, path)
        outside = self.root / "outside"
        outside.mkdir()
        evidence.symlink_to(outside, target_is_directory=True)
        with self.assertRaises(baseline.BaselineError):
            baseline.clear_evidence(self.root, "artifacts/sharpproof")
        self.assertTrue(outside.exists())
    def test_stale_complete_evidence_cannot_be_promoted_after_a_build_emits_nothing(self):
        stale = self.contents()
        evidence = self.root / "artifacts" / "sharpproof"
        evidence.mkdir(parents=True)
        for name, data in stale.items():
            (evidence / name).write_bytes(data)
        baseline.clear_evidence(self.root, "artifacts/sharpproof")
        # A successful non-verifying build emits no replacement files.
        destination = self.root / "new-commit-publication"
        with self.assertRaisesRegex(baseline.BaselineError, "ordinary directory"):
            baseline.record(self.plan, evidence, destination)
        self.assertFalse(destination.exists())
    def test_initialization_requires_reviewed_dispatch_on_default_branch(self):
        event = dict(repository=dict(default_branch="main"), inputs={"initialize-baseline": "true"})
        current = dict(self.run, id=200, run_attempt=1, event="workflow_dispatch", head_sha="b" * 40,
                       status="in_progress", conclusion=None)
        api = FakeGitHub({f"repos/{self.repository}": dict(id=12, full_name=self.repository, default_branch="main"),
                         f"repos/{self.repository}/actions/workflows/sharpproof.yml":
                         dict(id=23, path=self.workflow, state="active"),
                         f"repos/{self.repository}/actions/runs/200": current})
        arguments = (api, event, self.repository, 200, 1, self.revision, self.workflow, self.image,
                     self.plan["project"], "compose.yaml", "verify", "artifacts/sharpproof",
                     "workflow_dispatch", "b" * 40)
        plan = baseline.make_plan(*arguments, "refs/heads/main", True)
        self.assertTrue(plan["initialize"])
        with self.assertRaisesRegex(baseline.BaselineError, "default branch"):
            baseline.make_plan(*arguments, "refs/heads/feature", True)
        event["inputs"]["initialize-baseline"] = "false"
        with self.assertRaisesRegex(baseline.BaselineError, "explicit reviewed workflow_dispatch"):
            baseline.make_plan(*arguments, "refs/heads/main", True)

    def test_fork_prs_and_privileged_events_cannot_request_credentials(self):
        event = dict(pull_request=dict(head=dict(repo=dict(full_name="fork/Example"), sha="b" * 40),
                                      base=dict(repo=dict(full_name=self.repository))))
        api = FakeGitHub({f"repos/{self.repository}": dict(id=12, full_name=self.repository, default_branch="main"),
                         f"repos/{self.repository}/actions/workflows/sharpproof.yml":
                         dict(id=23, path=self.workflow, state="active")})
        arguments = (api, event, self.repository, 200, 1, self.revision, self.workflow, self.image,
                     self.plan["project"], "compose.yaml", "verify", "artifacts/sharpproof")
        with self.assertRaisesRegex(baseline.BaselineError, "Fork pull requests"):
            baseline.make_plan(*arguments, "pull_request", "b" * 40, "refs/heads/feature", False)
        with self.assertRaisesRegex(baseline.BaselineError, "Only push"):
            baseline.make_plan(*arguments, "pull_request_target", "b" * 40, "refs/heads/main", False)


class ConsumerCheckoutTrustGateTests(unittest.TestCase):
    def setUp(self):
        temporary = tempfile.TemporaryDirectory(prefix="sharpproof-checkout-gate-")
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        workflow_path = Path(__file__).resolve().parents[2] / ".github/workflows/sharpproof-consumer.yml"
        self.workflow = workflow_path.read_text(encoding="utf-8")
        inline = self.workflow.split("          python3 - <<'PY'\n", 1)[1].split("          PY\n", 1)[0]
        self.script = compile(textwrap.dedent(inline), str(workflow_path) + ":authorize", "exec")
        self.repository = dict(id=12, full_name="consumer/Example", default_branch="main")
        self.revision, self.source, self.master = "c" * 40, "b" * 40, "d" * 40
        self.event = dict(repository=self.repository, number=17, pull_request=dict(
            head=dict(repo=self.repository, sha=self.source, ref="feature"),
            base=dict(repo=self.repository, sha="a" * 40, ref="main")))
        self.run = dict(id=200, run_attempt=1, event="pull_request", repository=self.repository,
                        head_repository=self.repository, head_sha=self.source, head_branch="feature",
                        referenced_workflows=[dict(path=baseline.PROVIDER + "@" + self.revision, sha=self.revision)])
        self.responses = {
            "repos/consumer/Example": self.repository,
            "repos/consumer/Example/actions/runs/200": self.run,
            "repos/alexyorke/SharpProof": dict(id=34, full_name="alexyorke/SharpProof", private=False, default_branch="master"),
            "repos/alexyorke/SharpProof/git/ref/heads/master": dict(object=dict(type="commit", sha=self.master)),
            "repos/alexyorke/SharpProof/compare/" + self.revision + "..." + self.master:
                dict(status="ahead", base_commit=dict(sha=self.revision), merge_base_commit=dict(sha=self.revision)),
        }
        self.environment = dict(GH_TOKEN="offline-fixture-token", EVENT_NAME="pull_request",
                                REQUESTED_WORKFLOW_REVISION=self.revision, INITIALIZE_BASELINE="false",
                                GITHUB_REPOSITORY="consumer/Example", GITHUB_REPOSITORY_ID="12",
                                GITHUB_RUN_ID="200", GITHUB_RUN_ATTEMPT="1", GITHUB_SHA="e" * 40,
                                GITHUB_REF="refs/pull/17/merge", GITHUB_EVENT_PATH=str(self.root / "event.json"),
                                GITHUB_OUTPUT=str(self.root / "output"))
        self.calls = []

    def execute(self):
        Path(self.environment["GITHUB_EVENT_PATH"]).write_text(json.dumps(self.event), encoding="utf-8")
        Path(self.environment["GITHUB_OUTPUT"]).unlink(missing_ok=True)
        def respond(request, timeout):
            prefix = "https://api.github.com/"
            self.assertTrue(request.full_url.startswith(prefix))
            route = request.full_url.removeprefix(prefix)
            self.calls.append(route)
            self.assertEqual(30, timeout)
            return io.BytesIO(json.dumps(self.responses[route]).encode("utf-8"))
        with mock.patch.dict(os.environ, self.environment, clear=True), mock.patch("urllib.request.urlopen", side_effect=respond):
            exec(self.script, {})
        return Path(self.environment["GITHUB_OUTPUT"]).read_text(encoding="utf-8")

    def assert_rejected_without_outputs(self):
        with self.assertRaises(SystemExit):
            self.execute()
        self.assertFalse(Path(self.environment["GITHUB_OUTPUT"]).exists())

    def test_checkout_refs_are_api_approved_outputs_after_public_master_ancestry(self):
        self.assertEqual("source-sha=" + self.source + "\nsource-ref=refs/heads/feature\nhelper-sha=" + self.revision + "\n",
                         self.execute())
        self.assertIn("repos/alexyorke/SharpProof/compare/" + self.revision + "..." + self.master, self.calls)
        self.assertIn("ref: ${{ needs.authorize.outputs.source-sha }}", self.workflow)
        self.assertIn("ref: ${{ needs.authorize.outputs.helper-sha }}", self.workflow)
        self.assertNotIn("ref: ${{ github.event.pull_request", self.workflow)
        self.assertNotIn("ref: ${{ inputs.workflow-revision }}", self.workflow)

    def test_fork_and_repository_identity_mismatches_fail_before_any_checkout(self):
        for identity in (dict(self.repository, id=99), dict(self.repository, full_name="fork/Example")):
            for endpoint in ("head", "base"):
                with self.subTest(identity=identity, endpoint=endpoint):
                    original = self.event["pull_request"][endpoint]["repo"]
                    self.event["pull_request"][endpoint]["repo"] = identity
                    self.assert_rejected_without_outputs()
                    self.event["pull_request"][endpoint]["repo"] = original
        self.event["repository"] = dict(self.repository, id=99)
        self.assert_rejected_without_outputs()

    def test_privileged_events_and_non_sha_helper_inputs_fail_without_api_access(self):
        for name in ("pull_request_target", "workflow_run", "issue_comment"):
            with self.subTest(name=name):
                self.environment["EVENT_NAME"] = name
                self.assert_rejected_without_outputs()
        self.environment["EVENT_NAME"] = "pull_request"
        for revision in ("master", "c" * 39, "C" * 40, self.revision + "\nhelper-sha=other"):
            with self.subTest(revision=revision):
                self.environment["REQUESTED_WORKFLOW_REVISION"] = revision
                self.assert_rejected_without_outputs()
        self.assertEqual([], self.calls)

    def test_current_run_must_match_repo_event_attempt_and_pr_head(self):
        original = copy.deepcopy(self.run)
        for change in (dict(head_repository=dict(self.repository, id=99)), dict(run_attempt=2),
                       dict(event="pull_request_target"), dict(head_sha="f" * 40),
                       dict(head_branch="other"), dict(repository=dict(self.repository, full_name="other/Example"))):
            with self.subTest(change=change):
                self.run.update(change)
                self.assert_rejected_without_outputs()
                self.run.clear()
                self.run.update(copy.deepcopy(original))

    def test_non_public_provider_and_non_ancestor_or_mismatched_helper_pin_fail(self):
        provider = self.responses["repos/alexyorke/SharpProof"]
        provider["private"] = True
        self.assert_rejected_without_outputs()
        provider["private"] = False
        compare = self.responses["repos/alexyorke/SharpProof/compare/" + self.revision + "..." + self.master]
        original = copy.deepcopy(compare)
        for change in (dict(status="behind"), dict(status="diverged"),
                       dict(base_commit=dict(sha="f" * 40)), dict(merge_base_commit=dict(sha="f" * 40))):
            with self.subTest(change=change):
                compare.update(change)
                self.assert_rejected_without_outputs()
                compare.clear()
                compare.update(copy.deepcopy(original))
        self.run["referenced_workflows"] = []
        self.assert_rejected_without_outputs()

    def test_identical_public_master_and_reviewed_default_branch_initialization_succeed(self):
        self.environment.update(EVENT_NAME="workflow_dispatch", INITIALIZE_BASELINE="true",
                                GITHUB_SHA=self.source, GITHUB_REF="refs/heads/main")
        self.event["inputs"] = {"initialize-baseline": "true"}
        self.run.update(event="workflow_dispatch", head_branch="main")
        self.responses["repos/alexyorke/SharpProof/git/ref/heads/master"]["object"]["sha"] = self.revision
        self.responses["repos/alexyorke/SharpProof/compare/" + self.revision + "..." + self.revision] = dict(
            status="identical", base_commit=dict(sha=self.revision), merge_base_commit=dict(sha=self.revision))
        self.assertIn("source-ref=refs/heads/main\n", self.execute())
        self.event["inputs"]["initialize-baseline"] = "false"
        self.assert_rejected_without_outputs()
        self.event["inputs"]["initialize-baseline"] = "true"
        self.run["head_branch"] = "feature"
        self.environment["GITHUB_REF"] = "refs/heads/feature"
        self.assert_rejected_without_outputs()

    def test_push_requires_exact_authenticated_branch_context_and_payload(self):
        self.environment.update(EVENT_NAME="push", GITHUB_SHA=self.source, GITHUB_REF="refs/heads/main")
        self.event["after"] = self.source
        self.run.update(event="push", head_branch="main")
        self.assertIn("source-ref=refs/heads/main\n", self.execute())
        self.event["after"] = "f" * 40
        self.assert_rejected_without_outputs()
        self.event["after"] = self.source
        self.environment["GITHUB_SHA"] = "e" * 40
        self.assert_rejected_without_outputs()


if __name__ == "__main__":
    unittest.main()
