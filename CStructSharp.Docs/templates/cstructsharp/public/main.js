function improveThemeControl() {
  const toggle = document.querySelector(
    "a.dropdown-toggle[title='Change theme']:not([data-cstruct-accessible])",
  );
  if (!toggle) {
    return;
  }

  toggle.dataset.cstructAccessible = "true";
  toggle.setAttribute("role", "button");
  toggle.setAttribute("tabindex", "0");
  toggle.setAttribute("aria-label", "Change theme");
  toggle.addEventListener("keydown", (event) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      toggle.click();
    }
  });
}

function improveScrollableCodeRegions() {
  for (const code of document.querySelectorAll("article pre code")) {
    const isScrollable =
      code.classList.contains("lang-ebnf") ||
      code.scrollWidth > code.clientWidth ||
      code.scrollHeight > code.clientHeight;
    if (isScrollable && code.getAttribute("tabindex") !== "0") {
      code.setAttribute("tabindex", "0");
    }
  }
}

function improveCodeCopyControls() {
  for (const code of document.querySelectorAll("article pre > code")) {
    const pre = code.parentElement;
    if (!pre || pre.parentElement?.classList.contains("cstruct-code-block")) {
      continue;
    }

    const wrapper = document.createElement("div");
    wrapper.className = "cstruct-code-block";
    pre.replaceWith(wrapper);

    const button = document.createElement("button");
    button.type = "button";
    button.className = "btn btn-sm btn-secondary cstruct-copy";
    button.setAttribute("aria-label", "Copy code");
    button.textContent = "Copy";
    button.addEventListener("click", async () => {
      await navigator.clipboard.writeText(code.textContent || "");
      button.textContent = "Copied";
      button.setAttribute("aria-label", "Code copied");
      window.setTimeout(() => {
        button.textContent = "Copy";
        button.setAttribute("aria-label", "Copy code");
      }, 1500);
    });

    wrapper.append(button, pre);
  }
}

function getSiteRelativePath() {
  const relativeRoot = document.querySelector("meta[name='docfx:rel']")?.content || "./";
  const siteRoot = new URL(relativeRoot, document.baseURI);
  if (!window.location.pathname.startsWith(siteRoot.pathname)) {
    return null;
  }
  return decodeURIComponent(window.location.pathname.slice(siteRoot.pathname.length));
}

function improvePublicationMetadata() {
  const relativePath = getSiteRelativePath();
  if (!relativePath) {
    return;
  }

  if (!document.querySelector("link[rel='canonical']")) {
    const canonical = document.createElement("link");
    canonical.rel = "canonical";
    canonical.href = new URL(relativePath, "https://vvollers.github.io/cstructsharp/docs/").href;
    document.head.append(canonical);
  }

  if (relativePath === "api/CStructSharp.CStruct.html") {
    const contribution = document.querySelector(".contribution");
    if (contribution && !contribution.querySelector(".edit-link")) {
      const sourceLink = document.createElement("a");
      sourceLink.href =
        "https://github.com/vvollers/CStructSharp/blob/main/CStructSharp/CStruct.cs";
      sourceLink.className = "edit-link";
      sourceLink.textContent = "View source";
      contribution.append(sourceLink);
    }
    return;
  }

  const sourcePath = relativePath.replace(/\.html$/, ".md");
  if (!/^(?:404|index|api\/index|examples\/.+|guides\/.+|language\/.+|project\/.+)\.md$/.test(sourcePath)) {
    return;
  }

  const contribution = document.querySelector(".contribution");
  if (!contribution) {
    return;
  }
  const editLink = contribution.querySelector(".edit-link") || document.createElement("a");
  const encodedPath = sourcePath.split("/").map(encodeURIComponent).join("/");
  editLink.href =
    `https://github.com/vvollers/CStructSharp/edit/main/CStructSharp.Docs/${encodedPath}`;
  editLink.className = "edit-link";
  editLink.textContent = "Edit this page";
  if (!editLink.parentElement) {
    contribution.append(editLink);
  }
}

export default {
  start: () => {
    improveThemeControl();
    improveScrollableCodeRegions();
    improveCodeCopyControls();
    improvePublicationMetadata();
    const navbar = document.querySelector("#navbar");
    if (navbar) {
      new MutationObserver(improveThemeControl).observe(navbar, {
        childList: true,
        subtree: true,
      });
    }
    const article = document.querySelector("article");
    if (article) {
      new MutationObserver(() => {
        improveScrollableCodeRegions();
        improveCodeCopyControls();
      }).observe(article, {
        childList: true,
        subtree: true,
      });
    }
  },
};
