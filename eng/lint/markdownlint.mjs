import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

// Use the pinned rule engine directly. The CLI's glob dependency currently has
// an unpatched advisory; Git already supplies the source inventory we need.
const require = createRequire("/opt/sharpproof/lint/package.json");
const { lint } = await import(pathToFileURL(require.resolve("markdownlint/promise")));
const root = fileURLToPath(new URL("../../", import.meta.url));
const explicitFiles = process.argv.slice(2);
const files = explicitFiles.length ? explicitFiles : [...new Set(
  execFileSync("git", ["ls-files", "-z", "--cached", "--others", "--exclude-standard"],
    { cwd: root, encoding: "utf8" }).split("\0").filter(Boolean)
)].filter((file) => /\.md$/i.test(file)
  && !/(^|\/)(bin|obj|artifacts|node_modules)\//.test(file)
  // Pinned Arcade sources are attested by the repository layout gate.
  && !file.startsWith("eng/common/")
  && !file.startsWith("docs/generated/")
  && !/\.generated\.md$/i.test(file)
  && !/\/Corpus\/third-party\//.test(file)
  && existsSync(resolve(root, file)));
if (!files.length) throw new Error("No authored Markdown inputs found.");
const results = await lint({
  files: files.map((file) => resolve(root, file)),
  config: JSON.parse(readFileSync(new URL("../../.markdownlint.json", import.meta.url), "utf8")),
});
const issues = Object.entries(results).flatMap(([file, findings]) => findings.map((finding) =>
  `${file}:${finding.lineNumber} ${finding.ruleNames.join("/")}: ${finding.ruleDescription}`
  + (finding.errorDetail ? ` (${finding.errorDetail})` : "")));
if (issues.length) {
  console.error(issues.join("\n"));
  process.exitCode = 1;
} else {
  console.log(`Markdown: ${files.length} files checked, zero findings.`);
}
