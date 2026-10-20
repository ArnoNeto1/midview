// Configuração do Remotion CLI (studio e render).
// https://www.remotion.dev/docs/config
import { Config } from "@remotion/cli/config";
import { enableTailwind } from "@remotion/tailwind-v4";

Config.setVideoImageFormat("jpeg");
// Qualidade alta nos frames intermediários evita banding nos gradientes escuros.
Config.setJpegQuality(95);
Config.setOverwriteOutput(true);
Config.overrideBundlerConfig((current) => enableTailwind(current));
