// The shared app configuration; the generated test catalog is data, not authored source.
import { appEslintConfig } from "@cstructsharp/app-shared/eslint-config";

export default appEslintConfig({ ignores: ["src/generated/**"] });
