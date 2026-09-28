export type Theme = "dark" | "light";
export const THEME_KEY = "shiphdmap.theme";

// Its own key, not a field in the persisted editor/ui stores: a theme is a per-browser preference, and adding it
// to either store's schema would bump that schema and throw away everyone's saved view. try/catch because
// storage can be missing or refuse (private windows, blocked site data); dark is the answer then.
export function loadTheme(): Theme {
  try { return localStorage.getItem(THEME_KEY) === "light" ? "light" : "dark"; } catch { return "dark"; }
}

export function applyTheme(t: Theme, save = true): void {
  document.documentElement.dataset.theme = t;
  if (save) try { localStorage.setItem(THEME_KEY, t); } catch { /* the page still themes, it just won't remember */ }
}

/** Inline style for a `.badge`: the dot takes the state's colour, the label stays in text ink. */
export const dot = (color: string) => ({ "--dot": color }) as import("react").CSSProperties;
