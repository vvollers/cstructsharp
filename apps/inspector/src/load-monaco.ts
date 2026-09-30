/**
 * Loads the inspector's Monaco setup (`cstruct-language.ts`) on first use. The shared `LayoutEditor` receives this
 * function as its `loadMonaco` prop, so Monaco stays out of the entry chunk until the schema editor is shown.
 * @returns The setup module, whose `monaco` is the namespace with the CStruct language and editor worker registered.
 */
export const loadMonaco = () => import("./cstruct-language");
