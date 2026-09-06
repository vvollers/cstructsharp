/// <reference types="node" />
import { readFileSync, existsSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { lessons } from "./lessons";

describe("lesson catalog provenance", () => {
  it("keeps stable unique IDs, registered source scenarios, guides, and executable expectations", () => {
    const runner = readFileSync(
      resolve(process.cwd(), "../CStructSharp.Docs/examples/Program.cs"),
      "utf8",
    );
    expect(new Set(lessons.map((lesson) => lesson.id)).size).toBe(lessons.length);
    expect(lessons[0].id).toBe("header");
    for (const lesson of lessons) {
      expect(lesson.explanation, lesson.id).toBeTruthy();
      expect(lesson.explanation, lesson.id).not.toContain("undefined");
      expect(Object.keys(lesson.operations)).toEqual([lesson.operation]);
      expect(runner, lesson.id).toContain(`("${lesson.sourceScenario}",`);
      expect(
        existsSync(
          resolve(process.cwd(), "../CStructSharp.Docs", lesson.guide.replace(/\.html$/, ".md")),
        ),
        lesson.guide,
      ).toBe(true);
      for (const operation of Object.values(lesson.operations)) {
        expect(Object.keys(operation!.expected).length, lesson.id).toBeGreaterThan(0);
      }
    }
  });
});
