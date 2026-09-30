/**
 * Loads the explorer's Monaco setup (`monaco-layout.ts`) on first use. The shared `LayoutEditor` receives this
 * function as its `loadMonaco` prop, so Monaco stays out of the entry chunk until an editor is shown.
 * @returns The setup module, whose `monaco` is the namespace with the explorer's languages and workers registered.
 */
export const loadMonaco = () => import("./monaco-layout");
