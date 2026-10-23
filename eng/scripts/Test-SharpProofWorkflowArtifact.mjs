#!/usr/bin/env node
// Bind an immutable Actions artifact to its exact producer and workflow run.
import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";

function positive(value) {
  if (typeof value !== "string" || !/^[1-9][0-9]{0,15}$/.test(value)) {
    throw new Error("Expected a canonical positive identity.");
  }
  const number = Number(value);
  if (!Number.isSafeInteger(number)) throw new Error("Identity is out of range.");
  return number;
}

function validate(record, artifactId, name, producerAttempt, context) {
  const wantedId = positive(artifactId);
  const attempt = positive(producerAttempt);
  const currentAttempt = positive(context.GITHUB_RUN_ATTEMPT);
  const runId = positive(context.GITHUB_RUN_ID);
  const repositoryId = positive(context.GITHUB_REPOSITORY_ID);
  const head = context.SHARPPROOF_WORKFLOW_HEAD_SHA;
  if (attempt > currentAttempt || typeof head !== "string" || !/^[a-f0-9]{40}$/.test(head)) {
    throw new Error("Producer attempt or source identity is invalid.");
  }
  if (typeof name !== "string" || !name || /[\r\n\x00/]/.test(name)) {
    throw new Error("Expected an exact artifact name.");
  }
  const run = record?.workflow_run;
  const identities = [[record?.id, wantedId], [run?.id, runId], [run?.repository_id, repositoryId]];
  if (identities.some(([actual, wanted]) => !Number.isSafeInteger(actual) || actual !== wanted)) {
    throw new Error("Artifact owner or run does not match the producer.");
  }
  if (record.name !== name || run.head_sha !== head || record.expired !== false ||
      !Number.isSafeInteger(record.size_in_bytes) || record.size_in_bytes <= 0 ||
      typeof record.digest !== "string" || !/^sha256:[a-f0-9]{64}$/.test(record.digest)) {
    throw new Error("Artifact name, source, expiry, size, or digest is invalid.");
  }
}

function selfTest() {
  const context = { GITHUB_RUN_ATTEMPT: "2", GITHUB_RUN_ID: "123",
    GITHUB_REPOSITORY_ID: "456", SHARPPROOF_WORKFLOW_HEAD_SHA: "a".repeat(40) };
  const valid = { id: 789, name: "fixture-1", expired: false, size_in_bytes: 1,
    digest: "sha256:" + "b".repeat(64),
    workflow_run: { id: 123, repository_id: 456, head_sha: "a".repeat(40) } };
  validate(valid, "789", "fixture-1", "1", context);
  validate(valid, "789", "fixture-1", "2", context);
  const failures = [];
  for (const [field, value] of [["id", 790], ["id", "789"], ["name", "other"],
    ["expired", true], ["expired", null], ["size_in_bytes", 0], ["size_in_bytes", true],
    ["digest", "sha256:bad"], ["digest", null]]) {
    const changed = structuredClone(valid);
    changed[field] = value;
    failures.push([changed, "789", "fixture-1", "1", context]);
  }
  for (const [field, value] of [["id", 124], ["repository_id", 457], ["head_sha", "c".repeat(40)]]) {
    const changed = structuredClone(valid);
    changed.workflow_run[field] = value;
    failures.push([changed, "789", "fixture-1", "1", context]);
  }
  for (const attempt of ["0", "01", "3", "-1", "9007199254740992"]) {
    failures.push([valid, "789", "fixture-1", attempt, context]);
  }
  failures.push([valid, "0789", "fixture-1", "1", context]);
  for (const arguments_ of failures) assert.throws(() => validate(...arguments_));
  console.log(`Workflow artifact controls passed: ${failures.length + 2}.`);
}

function main() {
  const raw = process.argv.slice(2);
  if (raw.length === 1 && raw[0] === "--self-test") {
    selfTest();
    return;
  }
  const args = new Map();
  for (let index = 0; index < raw.length; index += 2) {
    const key = raw[index];
    if (!["--artifact-id", "--name", "--producer-attempt"].includes(key) ||
        args.has(key) || typeof raw[index + 1] !== "string") throw new Error("Invalid artifact arguments.");
    args.set(key, raw[index + 1]);
  }
  if (args.size !== 3) throw new Error("All artifact identities are required.");
  positive(args.get("--artifact-id"));
  const repository = process.env.GITHUB_REPOSITORY;
  if (typeof repository !== "string" || !/^[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(repository)) {
    throw new Error("Invalid repository identity.");
  }
  const response = execFileSync("gh", ["api", `repos/${repository}/actions/artifacts/${args.get("--artifact-id")}`],
    { encoding: "utf8", stdio: ["ignore", "pipe", "pipe"] });
  validate(JSON.parse(response), args.get("--artifact-id"), args.get("--name"),
    args.get("--producer-attempt"), process.env);
  console.log("Immutable workflow artifact binding verified.");
}

main();
