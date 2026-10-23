#!/usr/bin/env python3
"""Authenticate complete consumer evidence against an exact trusted Actions run."""

import argparse
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import sys
import urllib.error
import urllib.parse
import urllib.request
import zipfile

SHA = re.compile(r"[0-9a-f]{40}\Z")
IMAGE = re.compile(r"ghcr\.io/alexyorke/sharpproof-preview@sha256:[0-9a-f]{64}\Z")
WORKFLOW = re.compile(r"\.github/workflows/[A-Za-z0-9_.-]+\.ya?ml\Z")
PROVIDER = "alexyorke/SharpProof/.github/workflows/sharpproof-consumer.yml"
RECEIPT = "consumer-baseline.json"
REQUIRED = {"result.json", "request.json", "compiler-manifest.json"}
LIMITS = {"result.json": 16 << 20, "request.json": 16 << 20,
          "compiler-manifest.json": 32 << 20, "result.sarif": 16 << 20, RECEIPT: 64 << 10}
RECEIPT_KEYS = {"schemaVersion", "repository", "repositoryId", "workflowPath", "workflowId",
                "workflowRevision", "consumerImage", "project", "runId", "runAttempt",
                "sourceSha", "sourceBranch", "event", "kind", "files"}


class BaselineError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise BaselineError(message)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Duplicate JSON property in baseline metadata.")
        result[key] = value
    return result


def decode_json(data):
    try:
        return json.loads(data, object_pairs_hook=unique_object)
    except (UnicodeError, json.JSONDecodeError) as error:
        raise BaselineError("Invalid baseline JSON metadata.") from error


def relative_path(value, suffix=None):
    require(isinstance(value, str) and value and "\\" not in value and "\n" not in value,
            "Use a nonempty repository-relative path with forward slashes.")
    require(not value.startswith("-") and not any(ord(character) < 32 for character in value), "Unsafe repository-relative path.")
    path = PurePosixPath(value)
    require(not path.is_absolute() and str(path) == value and
            all(part not in (".", "..") for part in value.split("/")) and ":" not in value,
            "Paths must remain inside the checked-out repository.")
    if suffix:
        require(value.endswith(suffix), "Unexpected project path suffix.")
    return value


def artifact_name(project, workflow, run_id, attempt):
    identity = hashlib.sha256((workflow + "\n" + project).encode()).hexdigest()[:16]
    return f"sharpproof-baseline-{identity}-{run_id}-{attempt}"


def positive_id(value):
    return type(value) is int and value > 0


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, message, headers, new_url):
        return None


class GitHub:
    def __init__(self, token):
        require(bool(token), "An explicit Actions read credential is required.")
        self.token = token

    def json(self, route, query=None):
        require(route.startswith("repos/"), "Unexpected GitHub API route.")
        url = "https://api.github.com/" + route
        if query:
            url += "?" + urllib.parse.urlencode(query)
        request = urllib.request.Request(url, headers={
            "Authorization": "Bearer " + self.token,
            "Accept": "application/vnd.github+json", "User-Agent": "SharpProof-consumer-baseline",
            "X-GitHub-Api-Version": "2022-11-28"})
        try:
            with urllib.request.urlopen(request, timeout=60) as response:
                data = response.read((8 << 20) + 1)
            require(len(data) <= 8 << 20, "GitHub metadata exceeded its size limit.")
            return decode_json(data)
        except urllib.error.HTTPError as error:
            raise BaselineError(f"GitHub metadata request failed with HTTP {error.code}.") from None

    def archive(self, repository, artifact_id):
        # Do not forward the API bearer token to the signed artifact-storage URL.
        url = f"https://api.github.com/repos/{repository}/actions/artifacts/{artifact_id}/zip"
        request = urllib.request.Request(url, headers={"Authorization": "Bearer " + self.token,
            "Accept": "application/vnd.github+json", "User-Agent": "SharpProof-consumer-baseline"})
        try:
            response = urllib.request.build_opener(NoRedirect).open(request, timeout=60)
        except urllib.error.HTTPError as redirect:
            require(redirect.code == 302, "Artifact download did not return a signed redirect.")
            location = redirect.headers.get("Location", "")
            require(urllib.parse.urlsplit(location).scheme == "https", "Artifact redirect must use HTTPS.")
            response = urllib.request.urlopen(urllib.request.Request(location), timeout=60)
        with response:
            archive = response.read((96 << 20) + 1)
        require(0 < len(archive) <= 96 << 20, "Baseline archive exceeded its size limit.")
        return archive


def validate_run(run, plan, *, current=False):
    path = run.get("path", "")
    require(isinstance(path, str), "Invalid workflow path metadata.")
    bare_path, separator, reference = path.partition("@")
    branch = plan["sourceBranch"] if current else plan["baseBranch"]
    source_sha = plan["sourceSha"] if current else plan["baseSha"]
    references = {branch, "refs/heads/" + branch, source_sha}
    if current and plan["event"] == "pull_request":
        number = plan.get("pullRequestNumber")
        if positive_id(number):
            references.update((f"refs/pull/{number}/merge", f"refs/pull/{number}/head"))
    require(run.get("workflow_id") == plan["workflowId"] and bare_path == plan["workflowPath"] and
            (not separator or reference in references),
            "Baseline workflow identity does not match the caller workflow.")
    for key in ("repository", "head_repository"):
        repository = run.get(key) or {}
        require(repository.get("id") == plan["repositoryId"] and
                repository.get("full_name") == plan["repository"], "Baseline repository identity does not match.")
    require(run.get("head_sha") == (plan["sourceSha"] if current else plan["baseSha"]),
            "No baseline is accepted for a different commit.")
    require(run.get("head_branch") == (plan["sourceBranch"] if current else plan["baseBranch"]),
            "Baseline run is not from the expected base branch.")
    expected_reference = PROVIDER + "@" + plan["workflowRevision"]
    require(any(item.get("path") == expected_reference and item.get("sha") == plan["workflowRevision"]
                for item in run.get("referenced_workflows", [])),
            "Baseline reusable workflow is not pinned to the expected revision.")
    if current:
        require(run.get("id") == plan["runId"] and run.get("run_attempt") == plan["runAttempt"], "Current run/attempt identity differs.")
        require(run.get("event") == plan["event"], "Current workflow event identity differs.")
    else:
        require(run.get("event") in ("push", "workflow_dispatch") and
                run.get("status") == "completed" and run.get("conclusion") == "success" and
                run.get("id") != plan["runId"], "Baseline requires a trusted successful base-branch run.")


def make_plan(api, event, repository, run_id, attempt, revision, workflow, image, project,
              compose_file, service, evidence_directory, event_name, source_sha, source_ref, initialize):
    require(re.fullmatch(r"[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+", repository) is not None,
            "Invalid repository identity.")
    require(positive_id(run_id) and positive_id(attempt), "Run and attempt IDs must be positive integers.")
    require(SHA.fullmatch(revision) is not None and SHA.fullmatch(source_sha) is not None,
            "Workflow and source revisions must be exact 40-character commit SHAs.")
    require(WORKFLOW.fullmatch(workflow) is not None, "Caller workflow must be an exact .github/workflows YAML path.")
    require(IMAGE.fullmatch(image) is not None, "Use the qualified private preview image pinned by SHA-256.")
    relative_path(project, ".csproj")
    relative_path(compose_file)
    relative_path(evidence_directory)
    require(re.fullmatch(r"[A-Za-z0-9_./-]+", evidence_directory) is not None, "Evidence path contains unsupported property characters.")
    require(evidence_directory.startswith("artifacts/"), "Evidence output must be a dedicated artifacts subdirectory.")
    require(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]*", service) is not None, "Invalid Compose service.")
    require(event_name in ("push", "pull_request", "workflow_dispatch"),
            "Only push, pull_request and reviewed workflow_dispatch events are supported.")
    repo = api.json("repos/" + repository)
    require(repo.get("full_name") == repository and positive_id(repo.get("id")), "Repository authority is invalid.")
    workflow_info = api.json(f"repos/{repository}/actions/workflows/{workflow.rsplit('/', 1)[1]}")
    require(workflow_info.get("path") == workflow and positive_id(workflow_info.get("id")) and
            workflow_info.get("state") == "active", "Caller workflow authority is invalid.")
    branch = source_ref.removeprefix("refs/heads/")
    require(branch != source_ref and bool(branch), "Consumer runs require a branch ref.")
    base_sha, base_branch = source_sha, branch
    if event_name == "pull_request":
        pull = event.get("pull_request") or {}
        require((pull.get("head", {}).get("repo") or {}).get("full_name") == repository and
                (pull.get("base", {}).get("repo") or {}).get("full_name") == repository,
                "Fork pull requests cannot receive private package or image credentials.")
        require(pull.get("head", {}).get("sha") == source_sha, "Pull-request source SHA differs.")
        branch = pull["head"]["ref"]
        base_sha, base_branch = pull["base"]["sha"], pull["base"]["ref"]
    elif event_name == "push":
        require(event.get("after") == source_sha, "Push source SHA differs.")
        base_sha = event.get("before", "")
    if initialize:
        require(branch == repo.get("default_branch"), "Initial reviewed baseline must run on the repository default branch.")
        require(event_name == "workflow_dispatch" and
                str((event.get("inputs") or {}).get("initialize-baseline", "")).lower() == "true",
                "Initial baseline requires explicit reviewed workflow_dispatch initialize-baseline=true.")
    plan = dict(repository=repository, repositoryId=repo["id"], workflowPath=workflow,
                workflowId=workflow_info["id"], workflowRevision=revision, consumerImage=image,
                project=project, composeFile=compose_file, service=service, evidenceDirectory=evidence_directory,
                runId=run_id, runAttempt=attempt, sourceSha=source_sha, sourceBranch=branch,
                baseSha=base_sha, baseBranch=base_branch, event=event_name, initialize=initialize)
    if event_name == "pull_request":
        plan["pullRequestNumber"] = event.get("number")
    validate_run(api.json(f"repos/{repository}/actions/runs/{run_id}"), plan, current=True)
    plan["artifactName"] = artifact_name(project, workflow, run_id, attempt)
    return plan


def select_baseline(api, plan):
    require(SHA.fullmatch(plan["baseSha"] or "") is not None and plan["baseSha"] != "0" * 40,
            "No exact base commit exists. Run reviewed workflow_dispatch with initialize-baseline=true.")
    candidates = []
    for page in range(1, 11):
        response = api.json(f"repos/{plan['repository']}/actions/workflows/{plan['workflowId']}/runs",
                            {"branch": plan["baseBranch"], "head_sha": plan["baseSha"],
                             "status": "success", "per_page": 100, "page": page})
        require(response.get("total_count", 0) <= 1000, "Baseline search exceeds the GitHub result limit.")
        rows = response.get("workflow_runs", [])
        for row in rows:
            if row.get("event") not in ("push", "workflow_dispatch") or row.get("id") == plan["runId"]:
                continue
            validate_run(row, plan)
            require(positive_id(row.get("id")) and positive_id(row.get("run_attempt")), "Invalid baseline run identity.")
            candidates.append(row)
        if len(rows) < 100:
            break
    require(bool(candidates), "No trusted successful run matches the exact base SHA. Run reviewed workflow_dispatch with initialize-baseline=true on that base branch; current evidence is never a fallback baseline.")
    run = max(candidates, key=lambda row: (row.get("run_number", 0), row["run_attempt"]))
    # Re-read the selected immutable identity rather than trusting a name-only search result.
    run = api.json(f"repos/{plan['repository']}/actions/runs/{run['id']}")
    validate_run(run, plan)
    expected_name = artifact_name(plan["project"], plan["workflowPath"], run["id"], run["run_attempt"])
    response = api.json(f"repos/{plan['repository']}/actions/runs/{run['id']}/artifacts", {"per_page": 100})
    require(response.get("total_count", 0) <= 100, "Baseline artifact inventory exceeds its limit.")
    artifacts = [item for item in response.get("artifacts", []) if item.get("name") == expected_name]
    require(len(artifacts) == 1, "Expected exactly one complete baseline evidence artifact for this run attempt/project.")
    artifact_id = artifacts[0].get("id")
    require(positive_id(artifact_id), "Invalid baseline artifact ID.")
    artifact = api.json(f"repos/{plan['repository']}/actions/artifacts/{artifact_id}")
    binding = artifact.get("workflow_run") or {}
    require(artifact.get("id") == artifact_id and artifact.get("name") == expected_name and
            artifact.get("expired") is False and binding.get("id") == run["id"] and
            binding.get("repository_id") == plan["repositoryId"] and
            binding.get("head_repository_id") == plan["repositoryId"] and
            binding.get("head_sha") == plan["baseSha"] and binding.get("head_branch") == plan["baseBranch"],
            "Baseline artifact identity, expiration or repository binding is invalid.")
    require(isinstance(artifact.get("digest"), str) and re.fullmatch(r"sha256:[0-9a-f]{64}", artifact["digest"]) is not None,
            "Baseline artifact has no authenticated SHA-256 archive digest.")
    return run, artifact


def clear_evidence(workspace, evidence_directory):
    relative_path(evidence_directory)
    require(re.fullmatch(r"[A-Za-z0-9_./-]+", evidence_directory) is not None, "Evidence path contains unsupported property characters.")
    require(evidence_directory.startswith("artifacts/"), "Evidence output must be a dedicated artifacts subdirectory.")
    workspace = workspace.resolve(strict=True)
    target = workspace / evidence_directory
    require(target.resolve() == target and workspace in target.parents,
            "Evidence output must be an ordinary directory inside the consumer checkout.")
    if target.exists():
        require(target.is_dir(), "Evidence output must be a directory.")
        shutil.rmtree(target)

def read_regular(path, maximum):
    require(path.is_file() and not path.is_symlink() and 0 < path.stat().st_size <= maximum,
            "Evidence must contain bounded, nonempty regular files.")
    data = path.read_bytes()
    require(0 < len(data) <= maximum, "Evidence changed while reading it.")
    return data


def record(plan, source, destination):
    require(not destination.exists(), "Evidence publication destination must be new.")
    require(source.is_dir() and not source.is_symlink(), "Evidence source must be an ordinary directory.")
    contents = {name: read_regular(source / name, LIMITS[name]) for name in REQUIRED}
    if (source / "result.sarif").exists():
        contents["result.sarif"] = read_regular(source / "result.sarif", LIMITS["result.sarif"])
    receipt = {key: plan[key] for key in RECEIPT_KEYS - {"schemaVersion", "files", "kind"}}
    receipt.update(schemaVersion=1, kind="reviewed-initial" if plan["initialize"] else
                   "verified-successor" if plan["event"] in ("push", "workflow_dispatch") else "comparison",
                   files=[dict(path=name, bytes=len(data), sha256=hashlib.sha256(data).hexdigest())
                          for name, data in sorted(contents.items())])
    destination.mkdir(parents=True)
    for name, data in contents.items():
        (destination / name).write_bytes(data)
    (destination / RECEIPT).write_text(json.dumps(receipt, sort_keys=True, indent=2) + "\n", encoding="utf-8")
    return receipt


def validate_receipt(contents, plan, run):
    require(REQUIRED | {RECEIPT} <= set(contents) and set(contents) <= set(LIMITS),
            "Baseline archive is missing complete evidence or contains unexpected files.")
    receipt = decode_json(contents[RECEIPT])
    require(isinstance(receipt, dict) and set(receipt) == RECEIPT_KEYS and type(receipt["schemaVersion"]) is int and receipt["schemaVersion"] == 1,
            "Unsupported baseline receipt shape.")
    for key in ("repository", "repositoryId", "workflowPath", "workflowId", "workflowRevision", "consumerImage", "project"):
        require(receipt[key] == plan[key], "Baseline receipt inputs or qualified image are incompatible.")
    for key, value in (("runId", run["id"]), ("runAttempt", run["run_attempt"]),
                       ("sourceSha", plan["baseSha"]), ("sourceBranch", plan["baseBranch"]), ("event", run["event"])):
        require(receipt[key] == value, "Baseline receipt does not match its trusted run identity.")
    require((run["event"] in ("push", "workflow_dispatch") and receipt["kind"] == "verified-successor") or
            (run["event"] == "workflow_dispatch" and receipt["kind"] == "reviewed-initial"),
            "Baseline is not a successful successor or an explicitly reviewed initial baseline.")
    require(isinstance(receipt["files"], list), "Invalid baseline file inventory.")
    seen = set()
    for item in receipt["files"]:
        require(isinstance(item, dict) and set(item) == {"path", "bytes", "sha256"}, "Invalid baseline file entry.")
        name = item["path"]
        require(name in contents and name != RECEIPT and name not in seen, "Duplicate or unknown baseline file.")
        seen.add(name)
        data = contents[name]
        require(type(item["bytes"]) is int and item["bytes"] == len(data) and
                item["sha256"] == hashlib.sha256(data).hexdigest(), "Baseline evidence file digest or size differs.")
    require(seen == set(contents) - {RECEIPT}, "Baseline file inventory is incomplete.")
    return receipt


def unpack(archive, artifact, plan, run, destination):
    require(hashlib.sha256(archive).hexdigest() == artifact["digest"][7:], "Baseline archive digest differs from GitHub's artifact identity.")
    contents = {}
    try:
        with zipfile.ZipFile(io.BytesIO(archive)) as zipped:
            for item in zipped.infolist():
                name = item.filename
                require(name in LIMITS and name not in contents and not item.is_dir() and
                        not stat.S_ISLNK(item.external_attr >> 16) and
                        0 < item.file_size <= LIMITS[name], "Baseline archive contains an unsafe or oversized member.")
                contents[name] = zipped.read(item)
    except (zipfile.BadZipFile, RuntimeError) as error:
        raise BaselineError("Invalid baseline archive.") from error
    validate_receipt(contents, plan, run)
    require(not destination.exists(), "Baseline extraction destination must be new.")
    destination.mkdir(parents=True)
    for name, data in contents.items():
        (destination / name).write_bytes(data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest="command", required=True)
    prepare = commands.add_parser("plan")
    for name in ("repository", "run-id", "attempt", "revision", "workflow", "image", "project", "compose-file",
                 "service", "evidence-directory", "event-name", "source-sha", "source-ref", "event-file", "output"):
        prepare.add_argument("--" + name, required=True)
    prepare.add_argument("--initialize", action="store_true")
    resolve = commands.add_parser("resolve")
    resolve.add_argument("--plan", required=True)
    resolve.add_argument("--destination", required=True)
    clean = commands.add_parser("clean")
    clean.add_argument("--plan", required=True)
    clean.add_argument("--workspace", required=True)
    capture = commands.add_parser("record")
    capture.add_argument("--plan", required=True)
    capture.add_argument("--source", required=True)
    capture.add_argument("--destination", required=True)
    args = parser.parse_args()
    try:
        if args.command == "plan":
            plan = make_plan(GitHub(os.environ.get("GH_TOKEN", "")), decode_json(Path(args.event_file).read_bytes()),
                args.repository, int(args.run_id), int(args.attempt), args.revision, args.workflow, args.image,
                args.project, args.compose_file, args.service, args.evidence_directory, args.event_name,
                args.source_sha, args.source_ref, args.initialize)
            Path(args.output).write_text(json.dumps(plan, sort_keys=True, indent=2) + "\n", encoding="utf-8")
            if os.environ.get("GITHUB_OUTPUT"):
                with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as output:
                    output.write("artifact-name=" + plan["artifactName"] + "\n")
        else:
            plan = decode_json(Path(args.plan).read_bytes())
            if args.command == "clean":
                clear_evidence(Path(args.workspace), plan["evidenceDirectory"])
            elif args.command == "record":
                record(plan, Path(args.source), Path(args.destination))
            else:
                api = GitHub(os.environ.get("GH_TOKEN", ""))
                run, artifact = select_baseline(api, plan)
                unpack(api.archive(plan["repository"], artifact["id"]), artifact, plan, run, Path(args.destination))
                print(f"Validated exact-base baseline run {run['id']}, artifact ID {artifact['id']}.")
        return 0
    except (BaselineError, OSError, KeyError, TypeError, ValueError) as error:
        print("SharpProof baseline: " + str(error), file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
