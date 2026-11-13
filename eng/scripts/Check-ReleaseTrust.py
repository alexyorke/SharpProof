"""Fail closed unless this release's exact commit passed canonical master CI."""
import json
import os
import re
import urllib.request

REPOSITORY = "alexyorke/SharpProof"


def require(condition, message):
    if not condition:
        raise ValueError(message)


def api(route):
    request = urllib.request.Request("https://api.github.com/repos/" + REPOSITORY + "/" + route, headers={
        "Authorization": "Bearer " + os.environ["GH_TOKEN"],
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
    })
    with urllib.request.urlopen(request, timeout=30) as response:
        body = response.read((8 << 20) + 1)
    require(len(body) <= 8 << 20, "GitHub response exceeded its size limit")
    return json.loads(body)


def validate_run(run, workflow, sha, repository_id):
    require(run.get("workflow_id") == workflow["id"], "Unexpected workflow identity")
    require(run.get("path") == workflow["path"], "Unexpected workflow path")
    require(run.get("head_sha") == sha, "CI ran for a different commit")
    require(run.get("head_branch") == "master" and run.get("event") == "push",
            "CI must be a master push")
    require(run.get("repository", {}).get("id") == repository_id and
            run.get("head_repository", {}).get("id") == repository_id,
            "CI must originate in the canonical repository")
    require(run.get("status") == "completed" and run.get("conclusion") == "success",
            "Latest CI attempt did not succeed")


def check(sha, ref, repository, event, fetch=api):
    require(repository == REPOSITORY, "Only the canonical repository may release")
    require(event == "push" and re.fullmatch(r"refs/tags/v[0-9][A-Za-z0-9.+-]*", ref),
            "Publication requires a version-tag push; dispatch is always a dry run")
    require(re.fullmatch("[0-9a-f]{40}", sha), "Invalid release commit")
    repo = fetch("")
    require(repo["full_name"] == REPOSITORY and repo["default_branch"] == "master",
            "Canonical repository identity changed")
    comparison = fetch("compare/" + sha + "...master")
    require(comparison.get("status") in ("ahead", "identical") and
            comparison.get("merge_base_commit", {}).get("sha") == sha,
            "Release commit is not an ancestor of master")
    for filename in ("ci.yml", "codeql.yml"):
        workflow = fetch("actions/workflows/" + filename)
        require(workflow["path"] == ".github/workflows/" + filename and
                workflow["state"] == "active", "Expected workflow is not active")
        runs = fetch("actions/workflows/" + filename + "/runs?event=push&branch=master&head_sha=" +
                     sha + "&per_page=100")["workflow_runs"]
        require(bool(runs), "No canonical master run for " + filename)
        # Never accept an older success while a newer run/attempt failed or is pending.
        latest = max(runs, key=lambda run: run["id"])
        run = fetch("actions/runs/" + str(latest["id"]))
        validate_run(run, workflow, sha, repo["id"])
    print("Release commit passed canonical master CI and CodeQL: " + sha)


if __name__ == "__main__":
    check(os.environ["GITHUB_SHA"], os.environ["GITHUB_REF"],
          os.environ["GITHUB_REPOSITORY"], os.environ["GITHUB_EVENT_NAME"])
