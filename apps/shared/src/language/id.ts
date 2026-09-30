/**
 * The Monaco language id both editors use for layout source. It lives apart from `register.ts` so that code in the
 * entry chunk (the shared `LayoutEditor`) can name the language without bundling the vocabulary, which loads only
 * with Monaco.
 */
export const CSTRUCT_LANGUAGE_ID = "cstruct";
