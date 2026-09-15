mergeInto(LibraryManager.library, {
  EmitToWeb: function (name, json) {
    if (typeof window !== "undefined" && typeof window.dispatchReactUnityEvent === "function") {
      window.dispatchReactUnityEvent(UTF8ToString(name), UTF8ToString(json));
    }
  },
});
