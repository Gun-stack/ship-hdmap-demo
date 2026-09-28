import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { describe, expect, it } from "vitest";
import { PALETTE } from "./palette";

// The 3D slot fill and the plan-view slot must say "filled" in the same colour. Nothing generates one file from
// the other, so this reads Unity's Palette.cs as text and holds every shared name to the same hex.
const CS = resolve(import.meta.dirname, "../../../unity/Assets/ShipHdMap/Runtime/Core/Palette.cs");

function unityPalette(src: string): Record<string, string> {
  return Object.fromEntries([...src.matchAll(/public const string (\w+) = "(#[0-9a-fA-F]{6})";/g)].map((m) => [m[1], m[2].toLowerCase()]));
}

describe("palette sync with Unity", () => {
  const unity = unityPalette(readFileSync(CS, "utf8"));

  it("parses Palette.cs at all", () => {
    // Floor on what the regex found: if the file's format drifts and nothing matches, every per-name check below
    // would be skipped by an empty loop and this test would pass while checking nothing.
    expect(Object.keys(unity).length).toBeGreaterThanOrEqual(Object.keys(PALETTE).length);
  });

  it.each(Object.entries(PALETTE))("%s is the same colour in Unity", (name, hex) => {
    expect(unity[name], `Palette.cs has no ${name}`).toBeDefined();
    expect(unity[name]).toBe(hex.toLowerCase());
  });
});
