/**
 * A small XML reader for the well-formed, generator-written documents the tools inspect (Cobertura coverage,
 * TRX test results, NuGet nuspec, MSBuild projects): elements with attributes, text, and children. It handles
 * comments, processing instructions, CDATA, and the five predefined entities; it does not validate.
 */

/** @typedef {{ name: string, attributes: Record<string, string>, children: XmlElement[], text: string }} XmlElement */

export function parseXml(text) {
  let index = 0;
  const root = { name: "#document", attributes: {}, children: [], text: "" };
  const stack = [root];
  const decode = (value) =>
    value.replace(/&(lt|gt|amp|quot|apos|#x[0-9A-Fa-f]+|#\d+);/g, (entity, code) => {
      switch (code) {
        case "lt": return "<";
        case "gt": return ">";
        case "amp": return "&";
        case "quot": return '"';
        case "apos": return "'";
        default: return String.fromCodePoint(code[1] === "x" ? parseInt(code.slice(2), 16) : parseInt(code.slice(1), 10));
      }
    });
  while (index < text.length) {
    const open = text.indexOf("<", index);
    if (open < 0) {
      stack.at(-1).text += decode(text.slice(index));
      break;
    }
    if (open > index) stack.at(-1).text += decode(text.slice(index, open));
    if (text.startsWith("<!--", open)) {
      index = text.indexOf("-->", open) + 3;
    } else if (text.startsWith("<![CDATA[", open)) {
      const end = text.indexOf("]]>", open);
      stack.at(-1).text += text.slice(open + 9, end);
      index = end + 3;
    } else if (text.startsWith("<?", open) || text.startsWith("<!", open)) {
      index = text.indexOf(">", open) + 1;
    } else if (text.startsWith("</", open)) {
      index = text.indexOf(">", open) + 1;
      stack.pop();
    } else {
      const close = findTagEnd(text, open);
      const tag = text.slice(open + 1, close);
      const selfClosing = tag.endsWith("/");
      const body = selfClosing ? tag.slice(0, -1) : tag;
      const nameMatch = /^[^\s/>]+/.exec(body);
      const element = { name: nameMatch[0], attributes: {}, children: [], text: "" };
      const attributePattern = /([^\s=]+)\s*=\s*(?:"([^"]*)"|'([^']*)')/g;
      let attribute;
      while ((attribute = attributePattern.exec(body.slice(nameMatch[0].length))) !== null) {
        element.attributes[attribute[1]] = decode(attribute[2] ?? attribute[3]);
      }
      stack.at(-1).children.push(element);
      if (!selfClosing) stack.push(element);
      index = close + 1;
    }
  }
  return root;
}

function findTagEnd(text, open) {
  let quote = null;
  for (let index = open + 1; index < text.length; index++) {
    const character = text[index];
    if (quote) {
      if (character === quote) quote = null;
    } else if (character === '"' || character === "'") {
      quote = character;
    } else if (character === ">") {
      return index;
    }
  }
  throw new Error("Unterminated XML tag.");
}

/** Every descendant element (depth-first) whose local name matches, ignoring a namespace prefix. */
export function findAll(element, localName) {
  const found = [];
  const visit = (node) => {
    for (const child of node.children) {
      if (localNameOf(child.name) === localName) found.push(child);
      visit(child);
    }
  };
  visit(element);
  return found;
}

/** The first descendant with the local name, or null. */
export function findFirst(element, localName) {
  return findAll(element, localName)[0] ?? null;
}

/** Direct children with the local name. */
export function childrenNamed(element, localName) {
  return element.children.filter((child) => localNameOf(child.name) === localName);
}

export function localNameOf(name) {
  const colon = name.indexOf(":");
  return colon < 0 ? name : name.slice(colon + 1);
}
