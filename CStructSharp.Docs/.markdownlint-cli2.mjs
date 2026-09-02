export default {
  globs: [
    "**/*.md",
    "!_site/**",
    "!api/CStructSharp*.md",
    "!api-overwrites/**",
    "!node_modules/**",
  ],
  config: {
    "default": true,
    "MD013": false,
    "MD024": {
      "siblings_only": true,
    },
    "MD025": {
      "front_matter_title": "",
    },
    "MD033": {
      "allowed_elements": [
        "a",
        "br",
        "details",
        "kbd",
        "summary",
      ],
    },
    "MD041": false,
    "MD046": {
      "style": "fenced",
    },
  },
};
