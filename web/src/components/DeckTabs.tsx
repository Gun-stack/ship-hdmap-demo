import { useEditorStore } from "../store/editor";

export function DeckTabs() {
  const { decks, deckFilter, setDeckFilter } = useEditorStore();
  const ids = [...decks.map((d) => d.id), "all"];
  return (
    <div className="panel decktabs">
      <h4>갑판</h4>
      {ids.map((id) => <span key={id} className={deckFilter === id ? "on" : ""} onClick={() => setDeckFilter(id)}>{id === "all" ? "전체" : id}</span>)}
    </div>
  );
}
