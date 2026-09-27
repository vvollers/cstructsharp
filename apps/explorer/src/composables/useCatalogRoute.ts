import { computed, onMounted, onUnmounted, ref } from "vue";

import { isRunnable, type TestManifest } from "../demo-types";
import { lessons } from "../lessons";

/**
 * Keeps the selected lesson or test in step with the URL hash (`#lesson=<id>` or `#test=<id>`), so a selection can
 * be bookmarked and the browser's back button returns to the previous one. This is a Vue composable: call it from a
 * component's setup; it listens to hash and viewport changes while that component is mounted.
 * @param testManifest The generated catalog of managed tests.
 * @returns The mode, the selection, the route message for an unknown lesson, whether the catalog is expanded, and
 *   the functions the catalog calls.
 */
export function useCatalogRoute(testManifest: TestManifest) {
  const mode = ref<"learn" | "tests">("learn");
  const selectedLessonId = ref("header");
  const firstRunnable = testManifest.tests.find(isRunnable);
  const selectedTestId = ref(firstRunnable?.id ?? testManifest.tests[0]?.id ?? "");
  const routeMessage = ref("");

  // The catalog starts open on wide screens and closes after a lesson is chosen on narrow ones.
  const catalogExpanded = ref(window.innerWidth > 900);
  const narrowViewport = window.matchMedia("(max-width: 900px)");

  const selectedLesson = computed(() =>
    mode.value === "learn"
      ? (lessons.find((item) => item.id === selectedLessonId.value) ?? lessons[0]!)
      : null,
  );
  const selectedTest = computed(
    () =>
      selectedLesson.value ??
      testManifest.tests.find((test) => test.id === selectedTestId.value) ??
      null,
  );
  const selectedRunnable = computed(() =>
    isRunnable(selectedTest.value) ? selectedTest.value : null,
  );

  /**
   * Follows the viewport across the narrow breakpoint.
   * @param event The media-query change.
   */
  function updateCatalog(event: MediaQueryListEvent): void {
    catalogExpanded.value = !event.matches;
  }

  /** Selects the lesson or test the hash names; an unknown lesson falls back to the first one with a message. */
  function readRoute(): void {
    const route = new URLSearchParams(window.location.hash.slice(1));
    const lessonId = route.get("lesson");
    const testId = route.get("test");
    routeMessage.value = "";
    if (testId && testManifest.tests.some((item) => item.id === testId)) {
      mode.value = "tests";
      selectedTestId.value = testId;
    } else {
      mode.value = "learn";
      selectedLessonId.value = lessons.some((item) => item.id === lessonId) ? lessonId! : "header";
      if (lessonId && lessonId !== selectedLessonId.value)
        routeMessage.value = "That lesson was not found. Start with the header lesson below.";
    }
  }

  /**
   * Opens a lesson through the hash, so the route and the selection stay one state.
   * @param id The lesson id.
   */
  function selectLesson(id: string): void {
    window.location.hash = new URLSearchParams({ lesson: id }).toString();
    if (narrowViewport.matches) catalogExpanded.value = false;
  }

  /**
   * Opens a test through the hash.
   * @param id The test id; empty when the filtered list has no match.
   */
  function selectTest(id: string): void {
    selectedTestId.value = id;
    if (id) window.location.hash = new URLSearchParams({ test: id }).toString();
  }

  /**
   * Switches between the lessons and the full test list, keeping each list's last selection.
   * @param value The mode to show.
   */
  function changeMode(value: "learn" | "tests"): void {
    if (value === "learn") selectLesson(selectedLessonId.value);
    else selectTest(selectedTestId.value);
  }

  // The hash is read once on mount and again on every change, including back and forward navigation.
  onMounted(() => {
    readRoute();
    window.addEventListener("hashchange", readRoute);
    narrowViewport.addEventListener("change", updateCatalog);
  });
  // Removes the listeners the mount added.
  onUnmounted(() => {
    window.removeEventListener("hashchange", readRoute);
    narrowViewport.removeEventListener("change", updateCatalog);
  });

  return {
    mode,
    selectedLessonId,
    selectedLesson,
    selectedTestId,
    selectedTest,
    selectedRunnable,
    routeMessage,
    catalogExpanded,
    selectLesson,
    selectTest,
    changeMode,
  };
}
