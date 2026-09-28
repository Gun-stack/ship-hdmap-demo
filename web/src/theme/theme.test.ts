// @vitest-environment jsdom
import { beforeEach, describe, expect, it } from "vitest";
import { THEME_KEY, applyTheme, loadTheme } from "./theme";

describe("theme", () => {
  beforeEach(() => { localStorage.clear(); delete document.documentElement.dataset.theme; });
  it("defaults to dark and remembers light", () => {
    expect(loadTheme()).toBe("dark");
    applyTheme("light");
    expect(document.documentElement.dataset.theme).toBe("light");
    expect(localStorage.getItem(THEME_KEY)).toBe("light");
    expect(loadTheme()).toBe("light");
  });
  it("an unknown stored value is dark, not whatever it says", () => {
    localStorage.setItem(THEME_KEY, "sepia");
    expect(loadTheme()).toBe("dark");
  });
});
