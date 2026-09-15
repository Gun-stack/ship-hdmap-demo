import { useEditorStore } from "../store/editor";

export function StatusBar({ unityLoaded }: { unityLoaded: boolean }) {
  const { deckFilter, selectedId, error } = useEditorStore();
  const unsaved = useEditorStore((s) => Object.keys(s.drafts).length);
  return (
    <footer className="statusbar">
      <span>갑판 {deckFilter}</span>
      <span>선택 {selectedId ?? "-"}</span>
      <span>미저장 초안 {unsaved}</span>
      <span>Unity {unityLoaded ? "연결됨" : "로딩"}</span>
      {error && <span className="err">{error}</span>}
    </footer>
  );
}
