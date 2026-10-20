import { loadFont as loadInter } from "@remotion/google-fonts/Inter";
import { loadFont as loadJetBrainsMono } from "@remotion/google-fonts/JetBrainsMono";
import { loadFont as loadPlusJakartaSans } from "@remotion/google-fonts/PlusJakartaSans";
import { loadFont as loadQuicksand } from "@remotion/google-fonts/Quicksand";

// Só os pesos usados: menos requisições e nenhum timeout no render.
export const FONTS = {
  display: loadPlusJakartaSans("normal", { weights: ["500", "600", "700", "800"], subsets: ["latin"] }).fontFamily,
  ui: loadInter("normal", { weights: ["400", "500", "600", "700"], subsets: ["latin"] }).fontFamily,
  mono: loadJetBrainsMono("normal", { weights: ["500", "600", "700"], subsets: ["latin"] }).fontFamily,
  // Wordmark arredondado, como na logo original do midview.
  brand: loadQuicksand("normal", { weights: ["600", "700"], subsets: ["latin"] }).fontFamily,
};
