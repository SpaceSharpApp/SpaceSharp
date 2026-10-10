// Renders the README screenshots from tools/screenshot-mockup.html with headless Chromium.
//
//   npm i -D playwright && npx playwright install chromium
//   node tools/screenshots.js
//
// Each entry is a scene from the mockup tool plus its query string; the window is captured at 1600 x 900 and
// 1.5x so the PNGs stay crisp on GitHub. All names in them are sample data. Afterwards run
// tools/make-mobile-shots.py for the small WebP copies the landing page shows on phones.
const path = require("path");
const { chromium } = require("playwright");

const SHOTS = [
  ["screenshot.png", "scene=none&theme=dark&palette=Graphite"],
  ["shot-filter.png", "scene=drawer&theme=dark&palette=Graphite"],
  ["shot-change.png", "scene=compare&theme=dark&palette=Graphite"],
  ["shot-inspect.png", "scene=inspect&theme=dark&palette=Graphite"],
  ["shot-settings.png", "scene=settings&theme=dark&palette=Graphite"],
  ["shot-menu.png", "scene=menu&theme=dark&palette=Graphite"],
  ["shot-start.png", "scene=start&theme=dark&palette=Graphite"],
  ["shot-about.png", "scene=about&theme=dark&palette=Graphite"],
  ["shot-update.png", "scene=update&theme=dark&palette=Graphite"],
  ["shot-light.png", "scene=none&theme=light&palette=Pastel&mode=type"]
];

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage({ viewport: { width: 1680, height: 1040 }, deviceScaleFactor: 1.5 });
  const tool = "file://" + path.resolve(__dirname, "screenshot-mockup.html");
  for (const [file, query] of SHOTS) {
    await page.goto(`${tool}?${query}`, { waitUntil: "load" });
    await page.waitForTimeout(500);
    const win = await page.$("#host .ssm");
    await win.screenshot({ path: path.resolve(__dirname, "..", "docs", file) });
    console.log("  docs/" + file);
  }
  await browser.close();
})();
