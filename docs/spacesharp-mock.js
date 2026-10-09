/*
 * SpaceSharp drawn in a browser, with sample data.
 *
 * A MOCKUP, NOT THE APP. It imitates the window with the default settings closely enough for screenshots and
 * the website; it is not a build of SpaceSharp, so sizes, grouping, text fitting and the panel's numbers are
 * approximations, and it only follows UI changes when someone updates this file.
 *
 * One engine for two pages: the landing page (docs/index.html) embeds it as the live preview, and the screenshot
 * tool (tools/screenshot-mockup.html) wraps it in controls. It draws the 2.0 window: the toolbar, the path row
 * with the Filter button, the side panel, the map in its one look (folder frames with tinted title bars, cushioned
 * files, a one-pixel grid), the panel under the map (By type, Safe to clear, When changed), the filter drawer or
 * rail, and the dialogs. All names are generic sample data, so nothing real ends up in a screenshot.
 *
 *   const api = SpaceSharpMock.mount(hostElement, { theme: "dark", palette: "Graphite", scene: "none" });
 *   api.set("scene", "drawer");       // any key of api.state
 *   api.palettes("dark")              // palette names for a theme
 *   api.scenes                        // [[key, label], ...]
 */
(function () {
  "use strict";

  // ============================================================ styles for the window
  const CSS = `
  .ssm { --bg:#0D1117; --panel:#161B22; --control:#21262D; --hover:#2B3139; --stroke:#30363D; --text:#E6EDF3; --dim:#8B949E; --accent:#F5B82E; --accent-text:#1C1500; --mapbg:#0D1117; --titlebar:#161B22; --titletext:#fff; --menubg:#1C2128;
    position:relative; display:flex; flex-direction:column; overflow:hidden; background:var(--bg); color:var(--text); font:13px/1.3 "Segoe UI", system-ui, sans-serif; border-radius:8px; border:1px solid rgba(0,0,0,.35); box-shadow:0 24px 60px rgba(0,0,0,.35), 0 4px 14px rgba(0,0,0,.2); text-align:left; }
  .ssm * { box-sizing:border-box; }
  .ssm.light { --bg:#F3F3F6; --panel:#fff; --control:#ECECF1; --hover:#E2E2E9; --stroke:#D6D6DE; --text:#1C1C22; --dim:#61616F; --mapbg:#D9D9E1; --titlebar:#F3F3F3; --titletext:#1C1C22; --menubg:#fff; border-color:rgba(0,0,0,.18); }
  .ssm svg { display:block; }
  .ssm .titlebar { height:32px; flex:none; display:flex; align-items:center; background:var(--titlebar); color:var(--titletext); font-size:12px; }
  .ssm .titlebar .app { display:flex; align-items:center; gap:9px; padding-left:10px; flex:1; }
  .ssm .titlebar .app svg { width:16px; height:16px; }
  .ssm .caption { display:flex; height:100%; } .ssm .caption span { width:46px; display:grid; place-items:center; } .ssm .caption svg { width:10px; height:10px; stroke:currentColor; fill:none; stroke-width:1; }
  .ssm .toolbar { height:49px; flex:none; display:flex; align-items:center; gap:6px; padding:0 10px; background:var(--panel); border-bottom:1px solid var(--stroke); }
  .ssm .combo { height:32px; display:flex; align-items:center; gap:9px; padding:0 10px 0 12px; border-radius:6px; border:1px solid var(--stroke); background:var(--control); white-space:nowrap; overflow:hidden; }
  .ssm .combo .chev { margin-left:auto; width:10px; height:10px; color:var(--dim); flex:none; }
  .ssm .btn { height:32px; min-width:32px; display:flex; align-items:center; justify-content:center; gap:8px; padding:0 10px; border-radius:6px; white-space:nowrap; }
  .ssm .btn svg { width:16px; height:16px; flex:none; }
  .ssm .btn.accent { background:var(--accent); color:var(--accent-text); font-weight:600; padding:0 14px; }
  .ssm .btn.accent.only { padding:0 8px; }
  .ssm .btn.off { opacity:.35; }
  .ssm .btn.outline { border:1px solid var(--stroke); height:28px; }
  .ssm .btn.outline.on { border-color:var(--accent); }
  .ssm .divider { width:1px; height:20px; background:var(--stroke); margin:0 4px; flex:none; }
  .ssm .label { color:var(--dim); margin:0 8px 0 6px; white-space:nowrap; }
  .ssm .zoomlabel { width:64px; text-align:center; color:var(--dim); }
  .ssm .spacer { flex:1; }
  .ssm .swatches { display:flex; gap:1px; } .ssm .swatches i { width:8px; height:14px; border-radius:2px; display:block; }
  .ssm .crumbs { height:37px; flex:none; display:flex; align-items:center; padding:0 6px; background:var(--bg); border-bottom:1px solid var(--stroke); gap:2px; }
  .ssm .crumb { height:26px; display:flex; align-items:center; gap:6px; padding:0 6px; border-radius:6px; color:var(--dim); white-space:nowrap; }
  .ssm .crumb.current { color:var(--text); font-weight:600; } .ssm .crumb svg { width:15px; height:15px; }
  .ssm .crumbs .info { margin-left:auto; color:var(--dim); padding:0 4px 0 12px; white-space:nowrap; }
  .ssm .legend { display:none; gap:12px; margin-left:8px; padding-right:4px; } .ssm .legend.show { display:flex; }
  .ssm .legend span { display:flex; align-items:center; gap:5px; color:var(--dim); font-size:12px; white-space:nowrap; } .ssm .legend i { width:11px; height:11px; border-radius:3px; display:block; }
  .ssm .filterbtn { margin:0 4px 0 12px; }
  .ssm .filterbtn b { color:var(--accent); font-weight:600; }
  .ssm .rail { height:44px; flex:none; display:flex; align-items:center; gap:8px; padding:0 12px; background:var(--panel); border-bottom:1px solid var(--stroke); }
  .ssm .rail[hidden] { display:none; }
  .ssm .pill { height:28px; display:inline-flex; align-items:center; gap:6px; padding:0 10px; border-radius:14px; border:1px solid var(--stroke); color:var(--dim); white-space:nowrap; font-size:12.5px; }
  .ssm .pill.on { border-color:var(--accent); background:var(--control); color:var(--text); } .ssm .pill .k { color:var(--dim); } .ssm .pill i { width:8px; height:8px; border-radius:2px; display:inline-block; }
  .ssm .pill .chev { width:10px; height:10px; color:var(--dim); }
  .ssm .railname { height:28px; width:200px; display:flex; align-items:center; padding:0 10px; border-radius:14px; border:1px solid var(--stroke); color:var(--dim); font-size:12.5px; }
  .ssm .rail .count { margin-left:auto; color:var(--accent); font-weight:600; font-size:12.5px; white-space:nowrap; }
  .ssm .body { flex:1; display:flex; min-height:0; }
  .ssm .side { width:340px; flex:none; background:var(--panel); border-right:1px solid var(--stroke); display:flex; flex-direction:column; min-height:0; overflow:hidden; }
  .ssm .side.hidden { display:none; }
  .ssm .side h2 { font-size:15px; font-weight:600; margin:12px 14px 4px; } .ssm .side .sub { color:var(--dim); font-size:12px; margin:0 14px 6px; }
  .ssm .drives { padding:0 4px; }
  .ssm .drive { display:grid; grid-template-columns:auto 1fr auto; gap:0 10px; padding:9px 10px; border-radius:5px; margin:1px 0; }
  .ssm .drive svg { width:16px; height:16px; color:var(--dim); margin-top:1px; }
  .ssm .drive .name { font-weight:600; } .ssm .drive .free { color:var(--dim); }
  .ssm .drive .bar { grid-column:2 / span 2; height:6px; margin:7px 0 5px; border-radius:3px; background:var(--control); position:relative; }
  .ssm .drive .bar i { position:absolute; left:0; top:0; bottom:0; border-radius:3px; background:var(--accent); opacity:.9; }
  .ssm .drive .detail { grid-column:2 / span 2; color:var(--dim); font-size:11px; }
  .ssm .rule { height:1px; background:var(--stroke); margin:8px 14px 6px; }
  .ssm .tabs { display:flex; gap:2px; margin:4px 10px; } .ssm .tab { height:28px; padding:0 12px; display:flex; align-items:center; border-radius:6px; color:var(--dim); } .ssm .tab.on { background:var(--control); color:var(--text); font-weight:600; } .ssm .tab[hidden] { display:none; }
  .ssm .rows { padding:0 4px; overflow:hidden; }
  .ssm .row { display:grid; grid-template-columns:1fr auto; gap:0 10px; padding:6px 8px; border-radius:5px; margin:1px 0; position:relative; }
  .ssm .row > div { min-width:0; } .ssm .row .sw { position:absolute; left:8px; top:11px; width:9px; height:9px; border-radius:2px; } .ssm .row.chg { padding-left:24px; }
  .ssm .row .n { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; } .ssm .row .d { color:var(--dim); font-size:11px; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
  .ssm .row .s { font-weight:600; align-self:center; white-space:nowrap; } .ssm .row .bar { position:absolute; left:8px; bottom:2px; height:3px; border-radius:2px; background:var(--accent); opacity:.55; }
  .ssm .mapcol { flex:1; display:flex; flex-direction:column; min-width:0; min-height:0; }
  .ssm .map { flex:1; position:relative; background:var(--mapbg); min-width:0; min-height:0; overflow:hidden; }
  .ssm .map canvas { position:absolute; inset:0; width:100%; height:100%; display:block; }
  .ssm .sample-tag { position:absolute; right:12px; bottom:12px; pointer-events:none; padding:5px 11px; border-radius:999px; font-size:12px; font-weight:600; background:rgba(20,20,24,.78); color:#F2F2F6; border:1px solid rgba(255,255,255,.14); z-index:4; }
  .ssm .sample-tag[hidden] { display:none; }
  .ssm .bottom { flex:none; height:232px; background:var(--bg); border-top:1px solid var(--stroke); display:grid; grid-template-columns:5fr auto 4fr auto 4fr auto; }
  .ssm .bottom[hidden] { display:none; } .ssm .bottom.collapsed { height:36px; } .ssm .bottom.empty { height:58px; } .ssm .bottom.empty .sum { margin-bottom:0; }
  .ssm .bottom .vsep { width:1px; background:var(--stroke); margin:14px 0; }
  .ssm .bsec { padding:12px 18px 10px 18px; display:flex; flex-direction:column; min-width:0; overflow:hidden; }
  .ssm .bsec h3 { margin:0 0 2px; font-size:14px; font-weight:600; } .ssm .bsec .sum { color:var(--dim); font-size:12px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; margin-bottom:10px; }
  .ssm .bottom.collapsed .bsec .sum, .ssm .bottom.collapsed .bsec .bbody { display:none; }
  .ssm .bbody { flex:1; min-height:0; }
  .ssm .stack { display:flex; height:26px; border-radius:4px; overflow:hidden; background:var(--control); } .ssm .stack i { display:flex; align-items:center; height:100%; padding:0 6px; font-size:12px; color:#14141A; white-space:nowrap; overflow:hidden; }
  .ssm .tlegend { display:grid; grid-template-columns:1fr 1fr; gap:6px 16px; margin-top:12px; font-size:13px; } .ssm .tlegend span { display:flex; align-items:center; gap:7px; white-space:nowrap; overflow:hidden; } .ssm .tlegend i { width:10px; height:10px; border-radius:2px; flex:none; } .ssm .tlegend .z { color:var(--dim); font-variant-numeric:tabular-nums; white-space:nowrap; }
  .ssm .tlegend span.on { color:var(--accent); }
  .ssm .clean { display:grid; grid-template-columns:1fr auto auto auto; gap:4px 16px; align-items:center; font-size:13px; }
  .ssm .clean .nm { white-space:nowrap; overflow:hidden; text-overflow:ellipsis; } .ssm .clean .z { color:var(--accent); font-weight:600; font-variant-numeric:tabular-nums; white-space:nowrap; text-align:right; }
  .ssm .clean .lk, .ssm .clean .act { font-size:12px; height:24px; padding:0 8px; border-radius:6px; display:flex; align-items:center; justify-content:center; color:var(--text); }
  .ssm .also { color:var(--dim); font-size:12px; margin-top:6px; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
  .ssm .hist { display:flex; align-items:flex-end; gap:3px; height:64px; } .ssm .hist i { flex:1; display:block; border-radius:1px 1px 0 0; min-height:2px; } .ssm .axis { display:flex; justify-content:space-between; color:var(--dim); font-size:11.5px; margin-top:3px; }
  .ssm .bands { display:flex; gap:8px; margin-top:8px; } .ssm .band { flex:1; background:var(--control); border:1px solid transparent; border-radius:6px; padding:5px 7px; font-size:12px; line-height:1.3; min-width:0; } .ssm .band .nm { display:flex; align-items:center; gap:6px; color:var(--dim); white-space:nowrap; overflow:hidden; font-size:11.5px; } .ssm .band .nm span { overflow:hidden; text-overflow:ellipsis; } .ssm .band .nm i { width:9px; height:9px; border-radius:2px; flex:none; }
  .ssm .band b { display:block; font-weight:600; font-size:13px; white-space:nowrap; overflow:hidden; } .ssm .band.on { border-color:var(--accent); }
  .ssm .bchev { width:40px; display:grid; place-items:start center; padding-top:12px; color:var(--dim); } .ssm .bchev svg { width:14px; height:14px; }
  .ssm .status { height:30px; flex:none; display:flex; align-items:center; gap:16px; padding:0 12px; background:var(--bg); border-top:1px solid var(--stroke); }
  .ssm .status .hover { flex:1; min-width:0; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; } .ssm .status .scan { color:var(--dim); white-space:nowrap; }
  .ssm .ov { position:absolute; z-index:5; font-size:13px; color:var(--text); } .ssm .ov[hidden] { display:none; }
  .ssm .tip { background:var(--menubg); border:1px solid var(--stroke); border-radius:8px; padding:9px 12px; max-width:420px; box-shadow:0 6px 18px rgba(0,0,0,.45); }
  .ssm .tip .t { display:flex; align-items:baseline; gap:12px; } .ssm .tip .t .n { flex:1; font-weight:600; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; } .ssm .tip .t .z { font-size:14px; font-weight:600; }
  .ssm .tip .l { color:var(--dim); font-size:12px; margin-top:3px; } .ssm .tip .l b { color:var(--accent); font-weight:600; }
  .ssm .cmenu { background:var(--menubg); border:1px solid var(--stroke); border-radius:10px; padding:6px; width:300px; box-shadow:0 12px 32px rgba(0,0,0,.5); overflow:hidden; }
  .ssm .cmenu .mi { display:flex; align-items:center; gap:10px; height:32px; padding:0 10px; border-radius:6px; white-space:nowrap; } .ssm .cmenu .mi.primary { height:44px; background:var(--control); } .ssm .cmenu .mi.primary svg { color:var(--accent); width:16px; height:16px; } .ssm .cmenu .mi.primary .sub { display:block; color:var(--dim); font-size:11.5px; font-weight:400; overflow:hidden; text-overflow:ellipsis; max-width:200px; } .ssm .cmenu .foot { margin:4px -6px -6px; padding:6px; background:var(--panel); border-top:1px solid var(--stroke); } .ssm .cmenu .foot .mi { color:#FF7070; } .ssm .cmenu .foot .mi svg { color:#FF7070; }
  .ssm .cmenu .mi.on { background:var(--hover); } .ssm .cmenu .mi.off { opacity:.4; } .ssm .cmenu .mi b { font-weight:600; }
  .ssm .cmenu .mi .k { margin-left:auto; color:var(--dim); opacity:.8; font-size:11.5px; } .ssm .cmenu .mi .danger { color:#FF7070; }
  .ssm .cmenu .mi svg { width:14px; height:14px; color:var(--dim); flex:none; } .ssm .cmenu .mi .blank { width:14px; flex:none; }
  .ssm .cmenu .sep { height:1px; background:var(--stroke); margin:5px 10px; }
  .ssm .drawer { position:absolute; top:0; right:0; bottom:0; width:380px; z-index:5; display:flex; flex-direction:column; background:var(--panel); border-left:1px solid var(--stroke); box-shadow:-12px 0 30px rgba(0,0,0,.45); }
  .ssm .drawer[hidden] { display:none; }
  .ssm .drawer .dh { display:flex; align-items:center; height:44px; padding:0 16px; border-bottom:1px solid var(--stroke); flex:none; } .ssm .drawer .dh b { font-size:14px; } .ssm .drawer .dh .x { margin-left:auto; color:var(--dim); font-size:12px; display:flex; gap:14px; align-items:center; white-space:nowrap; } .ssm .drawer .dh .x svg { width:12px; height:12px; }
  .ssm .drawer .dbody { flex:1; padding:10px 16px; display:flex; flex-direction:column; gap:9px; overflow:hidden; }
  .ssm .drawer .sec { display:flex; flex-direction:column; gap:4px; } .ssm .drawer .sh { display:flex; justify-content:space-between; color:var(--dim); font-size:11.5px; text-transform:uppercase; letter-spacing:.04em; } .ssm .drawer .sh .v { color:var(--text); text-transform:none; letter-spacing:0; font-size:12.5px; }
  .ssm .drawer .trow { display:flex; align-items:center; gap:10px; height:24px; font-size:13px; } .ssm .drawer .trow .cb { width:14px; height:14px; border-radius:3px; border:1px solid var(--dim); flex:none; } .ssm .drawer .trow .cb.on { background:var(--accent); border-color:var(--accent); position:relative; } .ssm .drawer .trow .cb.on::after { content:""; position:absolute; left:4px; top:1px; width:4px; height:8px; border:solid var(--accent-text); border-width:0 2px 2px 0; transform:rotate(45deg); }
  .ssm .drawer .trow i { width:9px; height:9px; border-radius:2px; flex:none; } .ssm .drawer .trow .nm { width:78px; } .ssm .drawer .trow .bar { flex:1; height:6px; border-radius:3px; background:var(--control); overflow:hidden; } .ssm .drawer .trow .bar b { display:block; height:100%; } .ssm .drawer .trow .z { color:var(--dim); font-size:12px; width:60px; text-align:right; font-variant-numeric:tabular-nums; }
  .ssm .drawer .more { color:var(--accent); font-size:12px; height:22px; display:flex; align-items:center; }
  .ssm .drawer .slider { position:relative; height:22px; } .ssm .drawer .slider .tr { position:absolute; left:0; right:0; top:10px; height:4px; border-radius:2px; background:var(--stroke); } .ssm .drawer .slider .fl { position:absolute; top:10px; height:4px; border-radius:2px; background:var(--accent); } .ssm .drawer .slider .th { position:absolute; top:4px; width:16px; height:16px; margin-left:-8px; border-radius:8px; background:var(--accent); border:2px solid var(--bg); box-shadow:0 0 0 1px var(--accent); }
  .ssm .drawer .ticks { display:flex; justify-content:space-between; color:var(--dim); font-size:11px; }
  .ssm .drawer .seg { display:grid; grid-template-columns:repeat(4, 1fr); gap:2px; } .ssm .drawer .seg span { height:28px; display:flex; align-items:center; justify-content:center; border:1px solid var(--stroke); border-radius:6px; color:var(--dim); font-size:12px; } .ssm .drawer .seg span.on { background:var(--control); color:var(--text); border-color:var(--accent); }
  .ssm .drawer .seg.three { grid-template-columns:repeat(3, 1fr); }
  .ssm .drawer .chips { display:flex; flex-wrap:wrap; gap:6px; } .ssm .drawer .chip { height:25px; display:flex; align-items:center; padding:0 8px; border-radius:13px; border:1px solid var(--stroke); font-size:11.5px; color:var(--dim); white-space:nowrap; } .ssm .drawer .chip.on { border-color:var(--accent); color:var(--text); background:var(--control); }
  .ssm .drawer .df { display:flex; align-items:center; gap:10px; padding:12px 16px; border-top:1px solid var(--stroke); background:var(--bg); flex:none; } .ssm .drawer .df b { color:var(--accent); font-weight:600; } .ssm .drawer .df span { color:var(--dim); font-size:12px; } .ssm .drawer .df .btn { margin-left:auto; height:30px; }
  .ssm .scrim { position:absolute; inset:0; z-index:6; background:rgba(0,0,0,.45); display:grid; place-items:center; } .ssm .scrim[hidden] { display:none; }
  .ssm .dialog { width:640px; background:var(--bg); border:1px solid var(--stroke); border-radius:8px; box-shadow:0 30px 70px rgba(0,0,0,.6); overflow:hidden; color:var(--text); }
  .ssm .dialog .dt { height:32px; display:flex; align-items:center; padding:0 12px; gap:8px; background:var(--titlebar); color:var(--titletext); font-size:12px; } .ssm .dialog .dt svg { width:14px; height:14px; } .ssm .dialog .dt .x { margin-left:auto; }
  .ssm .dialog .df { display:flex; align-items:center; gap:6px; padding:12px 16px; background:var(--panel); border-top:1px solid var(--stroke); } .ssm .dialog .df .btn { border:1px solid var(--stroke); } .ssm .dialog .df .spacer { flex:1; } .ssm .dialog .df .btn.accent { min-width:84px; border-color:var(--accent); }
  .ssm .dialog.inspect .ih { padding:16px 24px 16px; border-bottom:1px solid var(--stroke); } .ssm .dialog.inspect .bc { font-size:11px; color:var(--dim); display:flex; gap:4px; margin-bottom:8px; } .ssm .dialog.inspect .bc a { color:var(--accent); }
  .ssm .dialog.inspect .nm2 { display:flex; align-items:center; gap:12px; } .ssm .dialog.inspect .nm2 .g { width:22px; height:22px; color:var(--accent); } .ssm .dialog.inspect .nm2 .n { font-size:19px; font-weight:600; line-height:1.1; } .ssm .dialog.inspect .nm2 .k { font-size:12px; color:var(--dim); margin-top:2px; } .ssm .dialog.inspect .nm2 .z { margin-left:auto; font-size:30px; font-weight:600; letter-spacing:-.5px; }
  .ssm .dialog.inspect .ctx { margin-top:14px; display:grid; grid-template-columns:22px 1fr; row-gap:7px; font-size:12.5px; align-items:start; } .ssm .dialog.inspect .ctx svg { width:14px; height:14px; color:var(--dim); margin-top:3px; } .ssm .dialog.inspect .ctx b { color:var(--accent); font-weight:600; } .ssm .dialog.inspect .ctx .d { color:var(--dim); }
  .ssm .dialog.inspect .ib { padding:14px 24px 20px; } .ssm .dialog.inspect .callout { background:var(--panel); border-left:3px solid var(--accent); padding:9px 12px; font-size:12.5px; margin-bottom:4px; } .ssm .dialog.inspect .callout b { color:var(--accent); font-weight:600; }
  .ssm .dialog.inspect .charts { display:grid; grid-template-columns:1fr 1fr; gap:20px; } .ssm .dialog.inspect h6 { font-size:12px; font-weight:400; color:var(--dim); margin:18px 0 8px; }
  .ssm .dialog.inspect .donut { display:flex; gap:14px; align-items:center; } .ssm .dialog.inspect .donut svg { width:104px; height:104px; flex:none; } .ssm .dialog.inspect .lg { display:grid; grid-template-columns:10px 1fr 46px; gap:8px; align-items:center; font-size:12px; padding:3px 4px; } .ssm .dialog.inspect .lg i { width:10px; height:10px; border-radius:2px; display:block; } .ssm .dialog.inspect .lg .p { text-align:right; color:var(--dim); }
  .ssm .dialog.inspect .wr { display:grid; grid-template-columns:200px 1fr 66px 44px; gap:12px; align-items:center; padding:7px 0; border-bottom:1px solid var(--control); font-size:13px; } .ssm .dialog.inspect .wr:last-of-type { border-bottom:0; } .ssm .dialog.inspect .wr .nmx { display:flex; gap:8px; align-items:center; min-width:0; } .ssm .dialog.inspect .wr .nmx svg { width:13px; height:13px; color:var(--dim); flex:none; } .ssm .dialog.inspect .wr .nmx span { white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
  .ssm .dialog.inspect .wr .bar { height:6px; border-radius:3px; background:var(--control); position:relative; } .ssm .dialog.inspect .wr .bar i { position:absolute; left:0; top:0; bottom:0; border-radius:3px; background:var(--accent); } .ssm .dialog.inspect .wr .s { text-align:right; } .ssm .dialog.inspect .wr .p { text-align:right; color:var(--dim); }
  .ssm .dialog.inspect .rem { font-size:11px; color:var(--dim); margin-top:8px; }
  .ssm .start { position:absolute; inset:0; background:var(--mapbg); display:flex; flex-direction:column; z-index:3; } .ssm .start[hidden] { display:none; }
  .ssm .start-center { flex:1; display:flex; align-items:center; justify-content:center; } .ssm .start-center > div { width:520px; }
  .ssm .start-head { display:flex; align-items:center; gap:14px; } .ssm .start-mark svg { width:44px; height:44px; }
  .ssm .start-title { font-size:18px; font-weight:600; } .ssm .start-sub { color:var(--dim); font-size:12.5px; margin-top:3px; }
  .ssm .start-card { margin-top:18px; background:var(--panel); border:1px solid var(--stroke); border-radius:10px; overflow:hidden; }
  .ssm .start-row { display:flex; align-items:center; gap:14px; height:42px; padding:0 12px 0 14px; font-size:13px; border-top:1px solid var(--stroke); } .ssm .start-row:first-child { border-top:0; }
  .ssm .start-row svg { width:14px; height:14px; color:var(--dim); } .ssm .start-row .n { flex:1; } .ssm .start-row .w, .ssm .start-row .s, .ssm .start-row .c { color:var(--dim); font-size:12.5px; white-space:nowrap; } .ssm .start-row .s { width:90px; text-align:right; }
  .ssm .start-actions { display:flex; gap:8px; margin-top:14px; } .ssm .start-actions .btn { border:1px solid var(--stroke); }
  .ssm .about-list { margin:0 24px; background:var(--panel); border:1px solid var(--stroke); border-radius:10px; overflow:hidden; }
  .ssm .about-row { display:flex; align-items:center; gap:14px; padding:9px 14px; border-top:1px solid var(--stroke); font-size:13.5px; } .ssm .about-row:first-child { border-top:0; } .ssm .about-row svg { width:16px; height:16px; color:var(--dim); } .ssm .about-row .sub { font-size:12px; color:var(--dim); margin-top:2px; } .ssm .about-row .c { margin-left:auto; color:var(--dim); }
  .ssm .dialog.about { width:520px; } .ssm .about-head { display:flex; align-items:center; gap:16px; padding:20px 24px 14px; } .ssm .about-head svg { width:56px; height:56px; } .ssm .about-head .t { font-size:22px; font-weight:600; line-height:1.1; } .ssm .about-head .s { color:var(--dim); font-size:13px; margin-top:4px; }
  .ssm .about-copy { padding:10px 24px 12px; font-size:11.5px; color:var(--dim); }
  .ssm .dialog.settings { width:660px; }
  .ssm .st-tabs { display:flex; gap:4px; padding:14px 22px 12px; border-bottom:1px solid var(--stroke); } .ssm .st-tabs span { padding:6px 12px; border-radius:6px; color:var(--dim); font-size:13px; } .ssm .st-tabs span.on { background:var(--control); color:var(--text); }
  .ssm .st-preview { margin:16px 22px 0; height:150px; border:1px solid var(--stroke); border-radius:8px; overflow:hidden; position:relative; background:var(--mapbg); } .ssm .st-preview canvas { position:absolute; inset:0; width:100%; height:100%; }
  .ssm .st-body { padding:14px 22px 10px; }
  .ssm .st-section { font-size:15px; font-weight:600; margin:0 0 4px; } .ssm .st-note { color:var(--dim); font-size:12px; margin-bottom:10px; }
  .ssm .st-card { background:var(--panel); border:1px solid var(--stroke); border-radius:10px; }
  .ssm .st-row { display:grid; grid-template-columns:1fr auto; gap:20px; align-items:center; padding:11px 16px; border-top:1px solid var(--stroke); } .ssm .st-row:first-child { border-top:0; }
  .ssm .st-row .t { font-size:13.5px; font-weight:600; } .ssm .st-row .d { color:var(--dim); font-size:12px; margin-top:2px; line-height:1.45; max-width:330px; }
  .ssm .st-chips { display:flex; gap:6px; } .ssm .st-chips span { height:26px; padding:0 12px; display:flex; align-items:center; border-radius:13px; border:1px solid var(--stroke); font-size:12px; color:var(--dim); } .ssm .st-chips span.on { border-color:var(--accent); color:var(--text); background:var(--control); }
  .ssm .st-slider { width:220px; } .ssm .st-slider .tr { height:4px; border-radius:2px; background:var(--hover); position:relative; margin:8px 0; } .ssm .st-slider .tr i { position:absolute; left:0; top:0; bottom:0; background:var(--accent); border-radius:2px; } .ssm .st-slider .tr b { position:absolute; top:50%; width:18px; height:18px; border-radius:50%; background:var(--accent); border:3px solid var(--bg); transform:translate(-50%,-50%); }
  .ssm .st-slider .ends { display:flex; justify-content:space-between; font-size:11px; color:var(--dim); }
  .ssm .sw { width:40px; height:22px; border-radius:11px; background:var(--accent); position:relative; } .ssm .sw::after { content:""; position:absolute; top:3px; right:3px; width:16px; height:16px; border-radius:50%; background:#1C1500; } .ssm .sw.off { background:var(--hover); } .ssm .sw.off::after { right:auto; left:3px; background:var(--dim); }
  .ssm .dialog.update { width:420px; } .ssm .ucol { padding:30px 32px 24px; display:flex; flex-direction:column; align-items:center; text-align:center; } .ssm .ucol .tile { width:84px; height:84px; border-radius:18px; background:var(--panel); display:grid; place-items:center; } .ssm .ucol .tile svg { width:64px; height:64px; } .ssm .ucol .t { font-size:19px; font-weight:600; margin-top:14px; } .ssm .ucol .h { color:var(--dim); margin-top:4px; } .ssm .ucol .jump { display:flex; align-items:center; gap:10px; margin-top:14px; font-size:12.5px; } .ssm .ucol .jump svg { width:14px; height:14px; color:var(--dim); } .ssm .ucol .from { padding:4px 12px; border-radius:999px; background:var(--control); color:var(--dim); } .ssm .ucol .to { padding:4px 12px; border-radius:999px; background:var(--accent); color:var(--accent-text); font-weight:600; } .ssm .ucol p { margin:16px 0 0; color:var(--dim); line-height:1.45; } .ssm .ucol .btn.big { width:100%; height:38px; margin-top:22px; font-size:13.5px; } .ssm .ucol .btn.wide { width:100%; height:34px; margin-top:8px; } .ssm .ucol a { color:var(--accent); font-size:12.5px; margin-top:12px; } .ssm .uhero { margin:20px 20px 0; padding:26px 24px 22px; background:var(--panel); border:1px solid var(--stroke); border-radius:10px; display:flex; flex-direction:column; align-items:center; text-align:center; } .ssm .uhero .mk svg { width:72px; height:72px; } .ssm .uhero .nm { display:flex; align-items:baseline; gap:10px; margin-top:10px; } .ssm .uhero .nm span { font-size:22px; font-weight:600; } .ssm .uhero .nm b { font-size:44px; font-weight:800; color:var(--accent); letter-spacing:-.03em; line-height:1; } .ssm .uhero .h { color:var(--dim); margin-top:6px; } .ssm .ulist { display:flex; flex-direction:column; gap:9px; padding:22px 36px 20px; font-size:13.5px; } .ssm .ulist div { display:flex; align-items:center; gap:12px; } .ssm .ulist svg { width:16px; height:16px; color:var(--accent); } .ssm .ulist a { color:var(--accent); font-size:12.5px; margin-left:28px; } .ssm .keeps { color:var(--dim); font-size:12px; } .ssm .dialog.notes { width:560px; }
  .ssm .dialog .ub { display:flex; gap:16px; padding:22px 24px 18px; } .ssm .dialog .ub .g { width:26px; height:26px; color:var(--accent); flex:none; margin-top:2px; }
  .ssm .dialog .ub .t { font-size:15px; font-weight:600; } .ssm .dialog .ub .h { color:var(--dim); margin-top:6px; line-height:1.45; }
  .ssm .dialog .nb { padding:20px 24px 16px; line-height:1.6; white-space:pre-wrap; max-height:380px; overflow:hidden; } .ssm .dialog .nb b { font-weight:600; }
  `;

  // ============================================================ icons
  const ICONS = {
    chev:'<path d="M2 4l4 4 4-4" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/>',
    chevUp:'<path d="M2 8l4-4 4 4" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/>',
    drive:'<rect x="1.5" y="5" width="13" height="6" rx="1.5" fill="none" stroke="currentColor" stroke-width="1.3"/><circle cx="11.5" cy="8" r=".9" fill="currentColor"/>',
    folder:'<path d="M1.5 4.2c0-.7.5-1.2 1.2-1.2h3.2l1.5 1.6h5.9c.7 0 1.2.5 1.2 1.2v6.5c0 .7-.5 1.2-1.2 1.2H2.7c-.7 0-1.2-.5-1.2-1.2z" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/>',
    file:'<path d="M4 2h5l3 3v9H4z" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M9 2v3h3" fill="none" stroke="currentColor" stroke-width="1.3"/>',
    refresh:'<path d="M13.2 8A5.2 5.2 0 1 1 11.6 4.2" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/><path d="M11.8 1.6v2.9H8.9" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/>',
    up:'<path d="M8 14V2.5M3.5 7L8 2.5 12.5 7" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/>',
    home:'<path d="M2.5 7.2L8 2.5l5.5 4.7V13a.8.8 0 0 1-.8.8H10V9.8H6v4H3.3a.8.8 0 0 1-.8-.8z" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/>',
    panel:'<rect x="1.5" y="3" width="13" height="10" rx="2" fill="none" stroke="currentColor" stroke-width="1.4"/><path d="M5.8 3v10" stroke="currentColor" stroke-width="1.4"/>',
    bottom:'<rect x="1.5" y="3" width="13" height="10" rx="2" fill="none" stroke="currentColor" stroke-width="1.4"/><path d="M1.5 9.4h13" stroke="currentColor" stroke-width="1.4"/>',
    zoomout:'<circle cx="6.8" cy="6.8" r="4.6" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M10.2 10.2L14 14M4.6 6.8h4.4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>',
    zoomin:'<circle cx="6.8" cy="6.8" r="4.6" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M10.2 10.2L14 14M4.6 6.8h4.4M6.8 4.6v4.4" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>',
    search:'<circle cx="6.8" cy="6.8" r="4.6" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M10.2 10.2L14 14" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>',
    funnel:'<path d="M2 3h12l-4.6 5.4V13l-2.8-1.4V8.4z" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/>',
    moon:'<path d="M13.5 9.6A5.8 5.8 0 0 1 6.4 2.5a5.8 5.8 0 1 0 7.1 7.1z" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/>',
    sun:'<circle cx="8" cy="8" r="3" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M8 1v1.6M8 13.4V15M1 8h1.6M13.4 8H15M3 3l1.1 1.1M11.9 11.9L13 13M13 3l-1.1 1.1M4.1 11.9L3 13" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>',
    gear:'<g transform="scale(.6667)" fill="none" stroke="currentColor" stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round"><path d="M12.22 2h-.44a2 2 0 0 0-2 2v.18a2 2 0 0 1-1 1.73l-.43.25a2 2 0 0 1-2 0l-.15-.08a2 2 0 0 0-2.73.73l-.22.38a2 2 0 0 0 .73 2.73l.15.1a2 2 0 0 1 1 1.72v.51a2 2 0 0 1-1 1.74l-.15.09a2 2 0 0 0-.73 2.73l.22.38a2 2 0 0 0 2.73.73l.15-.08a2 2 0 0 1 2 0l.43.25a2 2 0 0 1 1 1.73V20a2 2 0 0 0 2 2h.44a2 2 0 0 0 2-2v-.18a2 2 0 0 1 1-1.73l.43-.25a2 2 0 0 1 2 0l.15.08a2 2 0 0 0 2.73-.73l.22-.39a2 2 0 0 0-.73-2.73l-.15-.08a2 2 0 0 1-1-1.74v-.5a2 2 0 0 1 1-1.74l.15-.09a2 2 0 0 0 .73-2.73l-.22-.38a2 2 0 0 0-2.73-.73l-.15.08a2 2 0 0 1-2 0l-.43-.25a2 2 0 0 1-1-1.73V4a2 2 0 0 0-2-2z"/><circle cx="12" cy="12" r="3"/></g>',
    info:'<circle cx="8" cy="8" r="6.3" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M8 7.2v4" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/><circle cx="8" cy="4.9" r=".9" fill="currentColor"/>',
    x:'<path d="M4 4l8 8M12 4l-8 8" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round"/>',
    select:'<rect x="2" y="2" width="12" height="12" rx="2" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M5 8.2l2 2 4-4.4" fill="none" stroke="currentColor" stroke-width="1.4" stroke-linecap="round" stroke-linejoin="round"/>',
    props:'<rect x="2.5" y="2" width="11" height="12" rx="1.5" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M5 6h6M5 8.5h6M5 11h4" stroke="currentColor" stroke-width="1.2" stroke-linecap="round"/>',
    copy:'<rect x="5.5" y="5.5" width="8" height="8.5" rx="1.5" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M10.5 5.5V3.7a1.2 1.2 0 0 0-1.2-1.2H3.7a1.2 1.2 0 0 0-1.2 1.2v5.6a1.2 1.2 0 0 0 1.2 1.2h1.8" fill="none" stroke="currentColor" stroke-width="1.3"/>',
    trash:'<path d="M3 4.5h10M6.5 4.5V3h3v1.5M4.2 4.5l.6 8.3a1 1 0 0 0 1 .9h4.4a1 1 0 0 0 1-.9l.6-8.3" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/>',
    open:'<path d="M9.5 2.5h4v4M13.5 2.5L7.5 8.5M12 9.5v3a1 1 0 0 1-1 1H3.5a1 1 0 0 1-1-1V5a1 1 0 0 1 1-1h3" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/>',
    folderOpen:'<path d="M1.5 4.2c0-.7.5-1.2 1.2-1.2h3.2l1.5 1.6h5.9c.7 0 1.2.5 1.2 1.2v1.2H4.1L2 12.5" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/><path d="M2 12.5l2.1-5.5h10.4l-2 5.5z" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linejoin="round"/>',
    keyboard:'<rect x="1.5" y="4" width="13" height="8" rx="1.5" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M4 7h1M6.5 7h1M9 7h1M11.5 7h1M5 9.5h6" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>',
    bug:'<rect x="5" y="4" width="6" height="9" rx="3" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M8 4V2M2 8h3M11 8h3M3 12l2-1.5M13 12l-2-1.5M3 4l2 1.5M13 4l-2 1.5" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>',
    idea:'<path d="M6 13h4M6.5 11h3a4.5 4.5 0 1 0-3 0z" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round" stroke-linejoin="round"/>',
    globe:'<circle cx="8" cy="8" r="6" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M2 8h12M8 2c2 2 2 10 0 12M8 2c-2 2-2 10 0 12" fill="none" stroke="currentColor" stroke-width="1.3"/>',
    book:'<path d="M3 2.5h6l3 3v8H3z" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M9 2.5v3h3M5.5 8.5h5M5.5 11h5" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>',
    clock:'<circle cx="8" cy="8" r="6" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M8 4.5V8l2.5 1.5" fill="none" stroke="currentColor" stroke-width="1.3" stroke-linecap="round"/>',
    pie:'<circle cx="8" cy="8" r="6" fill="none" stroke="currentColor" stroke-width="1.3"/><path d="M8 2v6l4.2 2.4" fill="none" stroke="currentColor" stroke-width="1.3"/>',
    arrow:'<path d="M3 8h9M8.5 4l4 4-4 4" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>',
    check:'<path d="M3 8.5l3 3 7-7" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round"/>',
    download:'<path d="M8 2v8M4.5 6.5 8 10l3.5-3.5M3 12.5h10" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round"/>'
  };
  const svg = (name, cls) => `<svg viewBox="0 0 16 16"${cls ? ` class="${cls}"` : ""}>${ICONS[name]}</svg>`;
  // The mark: an amber block with a cube carved from its front corner (the 16 px shallow-carve version).
  const MARK = `<svg viewBox="0 0 100 100"><polygon points="50.00,10.00 86.37,31.00 69.05,41.00 50.00,30.00 30.95,41.00 13.63,31.00" fill="#FFD166"/><polygon points="13.63,31.00 30.95,41.00 30.95,63.00 50.00,74.00 50.00,94.00 13.63,73.00" fill="#F5B82E"/><polygon points="86.37,31.00 86.37,73.00 50.00,94.00 50.00,74.00 69.05,63.00 69.05,41.00" fill="#C98E22"/><polygon points="50.00,30.00 50.00,52.00 30.95,63.00 30.95,41.00" fill="#30363D"/><polygon points="50.00,30.00 69.05,41.00 69.05,63.00 50.00,52.00" fill="#161B22"/><polygon points="50.00,52.00 69.05,63.00 50.00,74.00 30.95,63.00" fill="#21262D"/></svg>`;

  // ============================================================ sample data
  const KB = 1024, MB = KB * 1024, GB = MB * 1024;
  let seed = 20260924;
  const rnd = () => (seed = (seed * 16807) % 2147483647) / 2147483647;
  const F = (name, bytes) => ({ name, size: Math.round(bytes), dir: false });
  const D = (name, children) => ({ name, dir: true, children });
  const many = (pattern, count, minMb, maxMb) => Array.from({ length: count }, (_, i) => F(pattern.replace("#", i + 1), (minMb + (maxMb - minMb) * Math.pow(rnd(), 2.4)) * MB));
  const range = (a, b) => Array.from({ length: b - a + 1 }, (_, i) => a + i);
  const pad = (n) => String(n).padStart(2, "0");

  const root = D("C:\\", [
    D("Users", [D("User", [
      D("Games", [
        D("Example Game", [
          D("data", [
            ...range(1, 12).map(i => F(`level_${pad(i)}.pak`, (1.3 + rnd() * 1.9) * GB)),
            ...range(1, 6).map(i => F(`cinematics_${pad(i)}.pak`, (1.1 + rnd() * 0.9) * GB)),
            F("intro.pak", 0.62 * GB), F("ui.pak", 0.54 * GB), F("scripts.pak", 0.21 * GB),
            D("saves", many("save_#.sav", 30, 0.4, 3))
          ]),
          D("bin", many("module_#.dll", 40, 0.1, 8)),
          F("ExampleGame.exe", 0.2 * MB)
        ]),
        D("Sample Game", [D("Build", [F("game_data.pak", 3.4 * GB), F("engine.dll", 31 * MB), D("assets", many("asset_#.bundle", 28, 20, 180))])])
      ]),
      D("Videos", [D("Recordings", [F("recording_01.mp4", 4.3 * GB), F("recording_02.mp4", 3.2 * GB), F("recording_03.mp4", 2.6 * GB), ...many("clip_#.mp4", 14, 60, 900)])]),
      D("Downloads", [F("os_installer.iso", 5.4 * GB), F("linux_image.iso", 5.7 * GB), F("setup.exe", 1.3 * GB), F("tools.zip", 0.18 * GB), ...many("archive_#.zip", 22, 2, 260), ...many("photo_#.jpg", 30, 1, 6)]),
      D("Pictures", [D("Screenshots", many("screenshot_#.png", 120, 0.4, 5)), D("Camera", many("photo_#.jpg", 160, 2, 9))]),
      D("Documents", [D("Saved Games", [D("Game One", many("save_#.dat", 40, 2, 9)), D("Game Two", many("save_#.dat", 30, 8, 24))]), ...many("document_#.docx", 18, 0.05, 2), ...many("report_#.pdf", 12, 0.2, 6)]),
      D("Projects", [D("Project A", many("source_#.cs", 16, 0.01, 0.06)), D("Project B", many("source_#.js", 60, 0.01, 0.2)), D("Project C", many("source_#.py", 30, 0.02, 0.5))]),
      D("AppData", [D("Local", [
        D("Shader Cache", many("cache_#.bin", 40, 8, 90)),
        D("Temp", many("temp_#.tmp", 80, 0.1, 40)),
        D("Web Browser", [D("User Data", [D("Default", [D("Cache", many("f_#", 60, 2, 40))])])]),
        D("Code Editor", many("index_#.dat", 40, 5, 60))
      ])])
    ])]),
    D("Games Library", [D("common", [
      D("Game One", [D("content", [...range(1, 14).map(i => F(`content_${pad(i)}.pak`, (1.2 + rnd() * 4.2) * GB)), ...many("expansion_#.pak", 8, 800, 3600)])]),
      D("Game Two", [D("Data", [...range(1, 12).map(i => F(`textures_${pad(i)}.pak`, (1.4 + rnd() * 2.6) * GB)), F("meshes.pak", 3.8 * GB), F("audio.pak", 4.9 * GB), F("world.dat", 0.46 * GB)])]),
      D("Game Three", [D("Data", [F("world.pak", 7.9 * GB), F("shared.pak", 3.1 * GB), F("textures.pak", 4.4 * GB), F("streaming.pak", 9.8 * GB), F("models.pak", 2.7 * GB), ...many("language_#.pak", 8, 100, 900)])])
    ])]),
    D("Program Files", [
      D("Code Editor", [D("lib", many("library_#.jar", 70, 2, 60)), D("plugins", many("plugin_#.jar", 90, 0.5, 30))]),
      D("Graphics Driver", many("driver_#.dll", 40, 5, 120)),
      D("Office Suite", [D("root", many("component_#.dll", 70, 1, 60))]),
      D("Runtime", [D("shared", many("library_#.dll", 90, 0.1, 12))])
    ]),
    D("Windows", [
      D("WinSxS", many("component_#", 260, 1, 80)),
      D("System32", many("driver_#.sys", 160, 0.1, 40)),
      D("Installer", many("package_#.msi", 30, 20, 400)),
      D("SoftwareDistribution", [D("Download", many("update_#.cab", 24, 30, 700))])
    ]),
    D("Windows.old", [D("Windows", [D("WinSxS", many("component_#", 120, 1, 60)), D("System32", many("driver_#.sys", 80, 0.1, 30))]), D("Users", [D("User", many("file_#.dat", 40, 1, 200))])]),
    D("ProgramData", [D("Package Cache", many("package_#.msi", 40, 5, 150)), D("Shared Data", many("data_#.bin", 40, 1, 80))]),
    D("$Recycle.Bin", [D("S-1-5-21", many("$R#.tmp", 25, 5, 400))]),
    F("pagefile.sys", 16 * GB), F("hiberfil.sys", 12.7 * GB), F("swapfile.sys", 256 * MB)
  ]);

  let totalDirs = 0;
  (function finish(node, parent) {
    node.parent = parent;
    node.path = !parent ? node.name : parent.parent ? `${parent.path}\\${node.name}` : parent.path + node.name;
    if (!node.dir) { node.files = 1; return; }
    totalDirs++;
    node.children.forEach(c => finish(c, node));
    node.children.sort((a, b) => b.size - a.size);
    node.size = node.children.reduce((s, c) => s + c.size, 0);
    node.files = node.children.reduce((s, c) => s + c.files, 0);
  })(root, null);
  const files = []; (function walk(n) { for (const c of n.children) c.dir ? walk(c) : files.push(c); })(root);
  files.sort((a, b) => b.size - a.size);

  const fmt = (bytes) => { const u = ["B", "KB", "MB", "GB", "TB"]; let v = bytes, i = 0; while (v >= 1024 && i < u.length - 1) { v /= 1024; i++; } return i === 0 ? `${bytes} B` : `${+v.toFixed(2)} ${u[i]}`; };
  const fmtShort = (bytes) => { const u = ["B", "KB", "MB", "GB", "TB"]; let v = bytes, i = 0; while (v >= 1024 && i < u.length - 1) { v /= 1024; i++; } return `${+v.toFixed(v >= 100 ? 0 : 1)} ${u[i]}`; };
  const num = (n) => n.toLocaleString("en-US");
  const pct = (a, b) => (100 * a / b).toFixed(1).replace(/\.0$/, "") + "%";

  // A pretend "last changed" per file, stable, spread over four years and weighted toward recent months.
  const monthsAgo = (n) => { let h = 7; for (const ch of n.path) h = (h * 31 + ch.charCodeAt(0)) | 0; const r = ((h >>> 0) % 1000) / 1000; return Math.floor(Math.pow(r, 1.6) * 48); };
  const ageBand = (n) => { const m = monthsAgo(n); return m < 1 ? 0 : m < 12 ? 1 : m < 36 ? 2 : 3; };
  const agoText = (n) => { const m = monthsAgo(n); return m < 1 ? "this month" : m < 12 ? `${m} months ago` : `${(m / 12).toFixed(1)} years ago`; };
  const changeOf = (n) => { let h = 0; for (const ch of n.name) h = (h * 31 + ch.charCodeAt(0)) | 0; const r = ((h >>> 0) % 100) / 100; return r < .12 ? null : r < .40 ? +Math.round(n.size * (r - .1)) : r < .62 ? -Math.round(n.size * (r - .4) * .6) : 0; };

  // ============================================================ colors
  const hex = (s) => [parseInt(s.slice(1, 3), 16), parseInt(s.slice(3, 5), 16), parseInt(s.slice(5, 7), 16)];
  const mix = (a, b, t) => a.map((v, i) => Math.round(v + (b[i] - v) * t));
  const rgb = (c) => `rgb(${c[0]},${c[1]},${c[2]})`;
  const rgba = (c, a) => `rgba(${c[0]},${c[1]},${c[2]},${a})`;
  function hsl(h, s, l) {
    h = (((h % 360) + 360) % 360) / 360; l = Math.min(1, Math.max(0, l));
    const f = (p, q, t) => { if (t < 0) t += 1; if (t > 1) t -= 1; if (t < 1 / 6) return p + (q - p) * 6 * t; if (t < .5) return q; if (t < 2 / 3) return p + (q - p) * (2 / 3 - t) * 6; return p; };
    if (s <= 0) return [l, l, l].map(v => Math.round(v * 255));
    const q = l < .5 ? l * (1 + s) : l + s - l * s, p = 2 * l - q;
    return [f(p, q, h + 1 / 3), f(p, q, h), f(p, q, h - 1 / 3)].map(v => Math.round(v * 255));
  }
  const lift = (list, t) => list.map(h => mix(hex(h), [255, 255, 255], t));
  const fromList = (theme, folders, tint, cats) => {
    const c = folders.map(x => typeof x === "string" ? hex(x) : x);
    return { theme, folder: d => c[d % c.length], file: d => mix(c[d % c.length], [255, 255, 255], tint), cats: cats ? cats.map(x => typeof x === "string" ? hex(x) : x) : [...Array(7)].map((_, i) => mix(c[i % c.length], [255, 255, 255], .2)).concat([hex("#B8B4AC")]) };
  };
  const CB = ["#0072B2", "#E69F00", "#009E73", "#CC79A7", "#56B4E9", "#D55E00", "#F0E442", "#4477AA", "#EE6677", "#228833", "#AA3377", "#66CCEE"];
  const CBC = ["#009E73", "#D55E00", "#CC79A7", "#E69F00", "#0072B2", "#56B4E9", "#F0E442", "#BBBBBB"];
  const PALETTES = {
    "Graphite": fromList("dark", ["#4F7CAC", "#B85C5C", "#5E9E6E", "#C99A3E", "#8A6BB8", "#3F9C9C", "#C06A8C", "#8FA14A", "#C77A45", "#5F74C9", "#A88A3E", "#4E9B82"], .42),
    "Ocean": fromList("dark", ["#2E6F95", "#3C8DAD", "#48A9A6", "#5FB49C", "#7FC8A9", "#4A7FB0", "#6C9BD2", "#3A9C9C", "#5C8ACF", "#2F8F8B", "#86B7D4", "#4FA3B8"], .5, ["#5FB49C", "#E07A5F", "#9C8ADE", "#F2C45A", "#3C8DAD", "#A9C6E0", "#7FC8A9", "#AFBCC6"]),
    "Sunset": fromList("dark", ["#C8553D", "#E07A5F", "#F2A65A", "#F6C85F", "#D9738A", "#B0588E", "#8E5A9E", "#E59A6B", "#CC6670", "#F1B36A", "#A86A9C", "#D98A5A"], .5, ["#8FBF7F", "#C8553D", "#B0588E", "#F6C85F", "#E07A5F", "#F2D4B8", "#8E5A9E", "#C9B8AE"]),
    "Forest": fromList("dark", ["#4F772D", "#6A994E", "#90A955", "#A7C957", "#7A9E7E", "#5C8D89", "#8C7A4F", "#9AB36A", "#5F8F55", "#B5A86A", "#6E9A77", "#87A33E"], .5, ["#A7C957", "#C8763D", "#9C7FB8", "#E3C567", "#5C8D89", "#D6DDB8", "#6A994E", "#B8B39E"]),
    "Vivid": fromList("dark", ["#4C8DFF", "#FF6B6B", "#2ECC71", "#FFC930", "#B463FF", "#1ABC9C", "#FF6FB5", "#F39C12", "#3498DB", "#9B59B6", "#27AE60", "#E74C3C"], .45),
    "Aurora": fromList("dark", ["#4FB3D9", "#E26A8A", "#5BC08E", "#E8B04B", "#9A7BD9", "#44B8B0", "#E27A55", "#86B64C", "#5A8FE0", "#D96AC0", "#D9A03F", "#4FAE8F"], .42, ["#5BC08E", "#E27A55", "#9A7BD9", "#E8B04B", "#4FB3D9", "#A9B8CC", "#44B8B0", "#8A93A3"]),
    "Monochrome": { theme: "dark", folder: d => hsl(230, .06, .36 + (d % 6) * .06), file: d => hsl(230, .05, .72 + (d % 4) * .05), cats: ["#D8D8DE", "#9A9AA6", "#B4B4BE", "#F5B82E", "#7E7E8A", "#E8E8EC", "#C4C4CC", "#A6A6B0"].map(hex) },
    "Color-blind safe": fromList("dark", CB, .45, CBC),
    "Pastel": fromList("light", ["#7FB3D5", "#F2A2A2", "#A8D8A8", "#F5CE84", "#C3A6E1", "#8FD3C7", "#F0B4D4", "#D4C58A", "#9FB9E8", "#F3B48A", "#B5D98B", "#D9A6C2"], .45, ["#78C890", "#E0806A", "#B795D6", "#EDC44D", "#6FA6DE", "#BAC4E2", "#6FC7B5", "#C8C0B4"]),
    "Paper": fromList("light", ["#C9B79C", "#A9C2C9", "#BFC9A6", "#D9B9A3", "#C2B3CF", "#A9CBBB", "#D6C3A3", "#B7C4D6", "#CDB5B5", "#B9CBA3", "#D2C1AD", "#A8BFC4"], .4),
    "Candy": fromList("light", ["#FF8FAB", "#FFB75E", "#FFE66D", "#8EE3B5", "#7FD1FF", "#B79CFF", "#FF9EE5", "#9CE0FF", "#FFD1A0", "#A8F0C6", "#C8B8FF", "#FFB3C6"], .4),
    "Nordic": fromList("light", ["#88C0D0", "#81A1C1", "#A3BE8C", "#EBCB8B", "#D08770", "#B48EAD", "#BF616A", "#5E81AC", "#8FBCBB", "#D8A657", "#A9B665", "#C98A8A"], .45),
    "Earth": fromList("light", ["#A47551", "#C2A878", "#8B9A6B", "#6E8B8B", "#B5856B", "#9C8A5A", "#7E9A7E", "#C9A86A", "#8A7A6A", "#A89A7A", "#6F8F7F", "#B39468"], .5),
    "Retro": fromList("light", ["#5B8DEF", "#49B86B", "#E8C547", "#E8864A", "#B565D9", "#4CC3C9", "#E86A8A", "#8CA64B", "#D97E3E", "#6A7FD9", "#C9A24A", "#5FB58F"], .45),
    "Color-blind safe (light)": fromList("light", lift(CB, .25), .4, lift(CBC, .25))
  };
  const CATS = ["Images", "Video", "Audio", "Archives", "Programs", "Documents", "Code", "Other"];
  const EXT = {}; [[0, "jpg png gif bmp webp psd dds tga"], [1, "mp4 mkv avi mov webm"], [2, "mp3 wav flac ogg wem bnk"], [3, "zip rar 7z iso cab pak archive ba2 bsa rpa bundle"], [4, "exe dll sys msi so pyd jar"], [5, "pdf doc docx txt md csv"], [6, "cs js ts php py json xml html css"]].forEach(([c, l]) => l.split(" ").forEach(e => EXT[e] = c));
  const category = (name) => { const m = /\.([^.]+)$/.exec(name); return m && EXT[m[1].toLowerCase()] !== undefined ? EXT[m[1].toLowerCase()] : 7; };
  const textFor = (c) => (0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]) > 128 ? [0x14, 0x14, 0x18] : [0xF2, 0xF2, 0xF6];
  const headerFill = (c, tint) => { const to = (0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]) > 96 ? [0, 0, 0] : [255, 255, 255]; return mix(c, to, tint / 100); };
  const SHADING = { Off: null, Soft: [30, 35, 55, 60], Classic: [55, 45, 50, 70], Deep: [85, 60, 45, 85] };   // cushion, height, brightness, scale

  // ============================================================ layout: squarified, with the app's grouping
  function squarify(nodes, x, y, w, h, emit) {
    const list = nodes.filter(n => n.size > 0), total = list.reduce((s, n) => s + n.size, 0);
    if (!list.length || w <= 0 || h <= 0) return;
    const scale = w * h / total; let i = 0;
    while (i < list.length) {
      const side = Math.min(w, h); if (side <= .0001) break;
      let rowSum = 0, worst = Infinity, j = i; const largest = list[i].size * scale;
      while (j < list.length) { const a = list[j].size * scale, s = rowSum + a; const nw = Math.max(side * side * largest / (s * s), (s * s) / (side * side * a)); if (j > i && nw > worst) break; rowSum = s; worst = nw; j++; }
      if (w >= h) { const t = rowSum / h; let cy = y; for (let k = i; k < j; k++) { const ih = list[k].size * scale / t; emit(list[k], x, cy, t, ih, k === j - 1, w - t < .5); cy += ih; } x += t; w = Math.max(0, w - t); }
      else { const t = rowSum / w; let cx = x; for (let k = i; k < j; k++) { const iw = list[k].size * scale / t; emit(list[k], cx, y, iw, t, h - t < .5, k === j - 1); cx += iw; } y += t; h = Math.max(0, h - t); }
      i = j;
    }
  }
  const singleSub = (n) => n.dir && n.children.length && n.children[0].dir && n.children[0].size > 0 && (n.children.length === 1 || n.children[1].size === 0) ? n.children[0] : null;
  const HEADER = 17, VISIBLE_AREA = 100;
  function grouped(folder, area, group) {
    const kids = folder.children, total = kids.reduce((s, c) => s + c.size, 0); if (total <= 0 || !group) return { kids, sole: false };
    // Like the app: an area quantized to a power of two so a smooth zoom does not shuffle the cut.
    const q = Math.pow(2, Math.floor(Math.log2(Math.max(1, area)))), per = q / total;
    let cutoff = kids.findIndex(c => c.size * per < VISIBLE_AREA); if (cutoff < 0) return { kids, sole: false };
    const rest = kids.slice(cutoff).filter(c => c.size > 0);
    const g = { name: `${num(rest.reduce((s, c) => s + c.files, 0))} files`, dir: false, group: true, parent: folder, size: rest.reduce((s, c) => s + c.size, 0), files: rest.reduce((s, c) => s + c.files, 0), members: rest };
    if (cutoff === 0) return { kids: [g], sole: true };
    if (rest.length < 2) return { kids, sole: false };
    return { kids: [...kids.slice(0, cutoff), g].sort((a, b) => b.size - a.size), sole: false };
  }
  function layout(node, x, y, w, h, depth, items, group, flushR = true, flushB = true, sole = false) {
    const x0 = Math.round(x), y0 = Math.round(y), x1 = Math.round(x + w), y1 = Math.round(y + h);
    if (x1 <= x0 || y1 <= y0) return;
    let shown = node, chainTop = null, next;
    while ((next = singleSub(shown))) { chainTop = chainTop || node; shown = next; }
    const container = shown.dir || (shown.group && !sole);
    const header = container && (x1 - x0) >= 30 && (y1 - y0) >= HEADER + 24 && !(shown.group && sole);
    const it = { node: shown, chainTop, x: x0, y: y0, w: x1 - x0, h: y1 - y0, depth, header, flushR, flushB, sole };
    items.push(it);
    if (!shown.dir) return;
    const sr = flushR ? 0 : 1, sb = flushB ? 0 : 1;
    const top = header ? HEADER : 0;
    const cw = it.w - sr, ch = it.h - sb - top;
    if (cw < 4 || ch < 4) return;
    const { kids, sole: soleKid } = grouped(shown, cw * ch, group);
    const first = items.length, sides = [];
    squarify(kids, x0, y0 + top, cw, ch, (c, cx, cy, iw, ih, fr, fb) => { if (iw >= 1 && ih >= 1) { const n = items.length; layout(c, cx, cy, iw, ih, depth + 1, items, group, fr, fb, soleKid); if (items.length > n) { items[n].child = true; sides.push(Math.min(items[n].w, items[n].h)); } } });
    // Like the app, the padding gap is decided per folder from the typical size of its children, so siblings agree.
    sides.sort((a, b) => a - b); const typical = sides.length ? sides[sides.length >> 1] : 0;
    for (let i = first; i < items.length; i++) if (items[i].child && items[i].typical === undefined) { items[i].typical = typical; items[i].child = false; }
  }

  // ============================================================ drawing
  function fit(ctx, text, maxW) {
    if (maxW < 8) return ""; if (ctx.measureText(text).width <= maxW) return text;
    let lo = 0, hi = text.length; while (lo < hi) { const mid = (lo + hi + 1) >> 1; if (ctx.measureText(text.slice(0, mid) + "…").width <= maxW) lo = mid; else hi = mid - 1; }
    return lo ? text.slice(0, lo) + "…" : "";
  }
  function chainName(it) {
    if (!it.chainTop) return it.node.name;
    const names = []; for (let n = it.node; n; n = n.parent) { names.unshift(n.name); if (n === it.chainTop) break; }
    return names.join("  ›  ");
  }
  const branchOf = new Map();
  root.children.forEach((c, i) => { (function mark(n) { branchOf.set(n, i); (n.children || []).forEach(mark); })(c); });

  function makeEngine(state) {
    const pal = () => PALETTES[state.palette] || PALETTES.Graphite;
    const cats = () => pal().cats;
    const mapBg = () => state.theme === "dark" ? [13, 17, 23] : [217, 217, 225];
    const gridColor = () => state.theme === "dark" ? [0x10, 0x10, 0x14] : [0x10, 0x10, 0x14];
    const changeFill = (it) => {
      const n = it.node; if (n.group) return [106, 106, 116];
      const d = n.dir ? n.children.reduce((s, c) => s + (changeOf(c) ?? c.size), 0) : changeOf(n);
      if (d === null) return [245, 184, 46];
      if (d === 0) return n.dir ? [85, 85, 96] : [106, 106, 116];
      const share = Math.min(1, Math.abs(d) / Math.max(n.size, 1)), t = Math.min(1, share * 1.4);
      return d > 0 ? mix([140, 122, 110], [232, 122, 79], t) : mix([110, 128, 140], [93, 169, 201], t);
    };
    const baseFill = (it) => {
      if (state.mode === "change") return changeFill(it);
      const p = pal();
      if (state.mode === "type") return it.node.dir ? hsl(225, .10, .50 + (it.depth % 6) * .05) : it.node.group ? mix(hsl(225, .1, .6), [255, 255, 255], .2) : cats()[category(it.node.name)];
      if (state.mode === "branch" && it.depth > 0) {
        const hue = p.folder((branchOf.get(it.node.group ? it.node.parent : it.node) ?? 0) % 12), level = Math.min(it.depth - 1, 6);
        return mix(hue, [255, 255, 255], it.node.dir ? level * .09 : .30 + level * .07);
      }
      return it.node.dir ? p.folder(it.depth % 12) : p.file(it.depth % 12);
    };
    const dim = (c) => { const g = Math.round(0.299 * c[0] + 0.587 * c[1] + 0.114 * c[2]); return mix(mix(c, [g, g, g], .6), mapBg(), .65); };
    const cushionFor = (ctx, it, small) => {
      const s = SHADING[state.shading]; if (!s) return null;
      const [cushion, height, brightness, scale] = s;
      let strength = cushion / 100 * Math.pow(scale / 100, it.depth); if (small) strength *= .55;
      const b = (brightness - 50) / 50, hiA = Math.min(1, .55 * strength * (1 + .8 * b)), loA = Math.min(1, .60 * strength * (1 - .8 * b));
      const mid = .25 + height / 100 * .55;
      const g = ctx.createLinearGradient(it.x, it.y, it.x + it.w, it.y + it.h);
      g.addColorStop(0, `rgba(255,255,255,${hiA.toFixed(3)})`); g.addColorStop(mid, "rgba(128,128,128,0)"); g.addColorStop(1, `rgba(0,0,0,${loA.toFixed(3)})`);
      return g;
    };
    const gapFor = (side) => { const g = state.padding; if (g <= 0 || side < 6) return 0; if (side < 12) return Math.min(g, 1); if (side < 40) return Math.min(g, 1 + (side - 12) / 28 * (g - 1)); return g; };
    function drawItem(ctx, raw, matches) {
      const n = raw.node;
      // The padding gap: each box pulls in from its bounds and the map background shows through.
      const gap = gapFor(raw.typical ?? Math.min(raw.w, raw.h));
      let it = raw;
      if (gap > 0) { if (raw.w <= gap || raw.h <= gap) return; const x0 = Math.round(raw.x + gap / 2), y0 = Math.round(raw.y + gap / 2), x1 = Math.round(raw.x + raw.w - gap / 2), y1 = Math.round(raw.y + raw.h - gap / 2); if (x1 <= x0 || y1 <= y0) return; it = { ...raw, x: x0, y: y0, w: x1 - x0, h: y1 - y0 }; }
      const sr = !state.grid || it.flushR ? 0 : 1, sb = !state.grid || it.flushB ? 0 : 1;
      const bw = it.w - sr, bh = it.h - sb; if (bw <= 0 || bh <= 0) return;
      let fill = baseFill(it);
      const dimmed = matches && !matches(n);
      if (dimmed) fill = dim(fill);
      ctx.fillStyle = rgb(fill); ctx.fillRect(it.x, it.y, bw, bh);
      if (!dimmed && !n.dir) { const g = cushionFor(ctx, { ...it, w: bw, h: bh }, Math.min(bw, bh) < 28); if (g) { ctx.fillStyle = g; ctx.fillRect(it.x, it.y, bw, bh); } }
      if (n.group) { ctx.fillStyle = "rgba(0,0,0,.14)"; ctx.fillRect(it.x, it.y, bw, bh); }
      ctx.fillStyle = rgb(gridColor());
      if (sr) ctx.fillRect(it.x + bw, it.y, 1, it.h);
      if (sb) ctx.fillRect(it.x, it.y + bh, bw, 1);
      const textCol = dimmed ? mix(mapBg(), [128, 128, 136], .6) : state.blackText ? [0, 0, 0] : textFor(fill);
      const FONT = "'Segoe UI Variable Text', 'Segoe UI', system-ui, sans-serif";
      if (n.dir || (n.group && it.header)) {
        if (!it.header) return;
        const bar = dimmed ? fill : headerFill(fill, state.tint);
        ctx.fillStyle = rgb(bar); ctx.fillRect(it.x, it.y, bw, HEADER);
        if (state.grid) { ctx.fillStyle = rgb(gridColor()); ctx.fillRect(it.x, it.y + HEADER - 1, bw, 1); }
        if (bw - 8 >= 18) {
          const tail = state.counts && n.files > 0 ? `  ·  ${num(n.files)} files` : "";
          ctx.fillStyle = rgb(dimmed ? textCol : state.blackText ? [0, 0, 0] : textFor(bar)); ctx.font = `600 11px ${FONT}`; ctx.textBaseline = "top"; ctx.textAlign = "left"; ctx.fillText(fit(ctx, `${chainName(it)}  ·  ${fmt(n.size)}${tail}`, bw - 8), it.x + 4, it.y + 2);
        }
        return;
      }
      if (bw < 44 || bh < 16) return;
      ctx.fillStyle = rgb(textCol); ctx.font = `11px ${FONT}`; ctx.textAlign = "center"; ctx.textBaseline = "top";
      const cx = it.x + bw / 2, tw = bw - 6;
      if (bh >= 34) { const ty = it.y + (bh - 30) / 2; ctx.fillText(fit(ctx, n.name, tw), cx, ty + 1); ctx.fillText(fit(ctx, fmt(n.size), tw), cx, ty + 16); }
      else ctx.fillText(fit(ctx, n.name, tw), cx, it.y + (bh - 15) / 2 + 1);
    }
    function render(canvas, w, h, node, matches, group) {
      const dpr = window.devicePixelRatio || 1;
      canvas.width = Math.round(w * dpr); canvas.height = Math.round(h * dpr);
      const ctx = canvas.getContext("2d"); ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.fillStyle = rgb(mapBg()); ctx.fillRect(0, 0, w, h);
      const items = []; layout(node, 0, 0, w, h, 0, items, group);
      for (const it of items) drawItem(ctx, it, matches);
      return items;
    }
    return { render, cats, pal, mapBg, baseFill };
  }

  // ============================================================ insights for the panel under the map
  const typeShares = () => { const t = Array(8).fill(0); for (const f of files) t[category(f.name)] += f.size; return t; };
  const monthBuckets = () => { const b = Array(36).fill(0); for (const f of files) { const m = monthsAgo(f); b[Math.min(35, m)] += f.size; } return b; };
  const bandSums = () => { const b = [0, 0, 0, 0]; for (const f of files) b[ageBand(f)] += f.size; return b; };
  const findDir = (name) => { let hit = null; (function walk(n) { if (hit) return; if (n.dir && n.name === name) { hit = n; return; } (n.children || []).forEach(walk); })(root); return hit; };
  const cleanupRows = () => {
    const rows = [];
    const add = (name, node, action) => { if (node) rows.push({ name, size: node.size, action, node }); };
    add("Windows.old", findDir("Windows.old"), "Recycle");
    add("Recycle Bin", findDir("$Recycle.Bin"), "Empty");
    add("Hibernation and paging files", { size: files.filter(f => /\.sys$/.test(f.name) && f.parent === root).reduce((s, f) => s + f.size, 0) }, "How to");
    add("Temporary files", findDir("Temp"), "Recycle");
    add("Shader caches", findDir("Shader Cache"), "Recycle");
    add("Windows Update downloads", findDir("SoftwareDistribution"), "Recycle");
    add("Browser caches", findDir("Cache"), "Recycle");
    const dl = findDir("Downloads"); if (dl) add("Downloads, untouched for a year", { size: dl.children.filter(c => !c.dir && ageBand(c) >= 2).reduce((s, c) => s + c.size, 0) }, "Review");
    return rows.sort((a, b) => b.size - a.size);
  };

  // ============================================================ the window
  const SCENES = [["none", "Map"], ["hover", "Hover card"], ["menu", "Right-click"], ["drawer", "Filter drawer"], ["rail", "Filter rail"], ["panel", "Panel: by type"], ["cleanup", "Panel: safe to clear"], ["timeline", "Panel: when changed"], ["inspect", "Inspect"], ["multi", "Selection"], ["zoomed", "Zoomed in"], ["compare", "Since last scan"], ["settings", "Settings"], ["update", "Update notice"], ["whatsnew", "What's new"], ["start", "Start screen"], ["about", "About"]];
  const MODES = [["branch", "Top folder"], ["depth", "Depth"], ["type", "File type"], ["change", "Change"]];

  function mount(host, opts) {
    if (!document.getElementById("ssm-css")) { const st = document.createElement("style"); st.id = "ssm-css"; st.textContent = CSS; document.head.appendChild(st); }
    const state = Object.assign({ theme: "dark", palette: "Graphite", mode: "depth", shading: "Off", padding: 2, grid: false, tint: 0, blackText: true, counts: true, scene: "none", side: true, bottom: "on", group: true, tag: true, w: 1600, h: 900, version: "2.0.0", interactive: true }, opts || {});
    const E = makeEngine(state);
    host.innerHTML = `<div class="ssm">
      <div class="titlebar"><div class="app"><span class="mark">${MARK}</span><span class="title">SpaceSharp ${state.version}</span></div>
        <div class="caption"><span><svg viewBox="0 0 10 10"><path d="M0 5.5h10"/></svg></span><span><svg viewBox="0 0 10 10"><rect x=".5" y=".5" width="9" height="9"/></svg></span><span><svg viewBox="0 0 10 10"><path d="M0 0l10 10M10 0L0 10"/></svg></span></div></div>
      <div class="toolbar">
        <div class="btn accent">${svg("folder")}Scan folder</div><div class="btn">${svg("refresh")}</div><div class="divider"></div>
        <div class="btn off nav">${svg("up")}</div><div class="btn off nav">${svg("home")}</div><div class="btn sidebtn">${svg("panel")}</div><div class="btn botbtn">${svg("bottom")}</div><div class="divider"></div>
        <div class="btn off nav">${svg("zoomout")}</div><div class="zoomlabel">100%</div><div class="btn">${svg("zoomin")}</div>
        <div class="spacer"></div>
        <span class="label">Color by</span><div class="combo" style="width:120px"><span class="modeText">Depth</span>${svg("chev", "chev")}</div>
        <span class="label" style="margin-left:8px">Palette</span><div class="combo" style="width:250px"><span class="swatches"></span><span class="paletteText"></span>${svg("chev", "chev")}</div><div class="divider"></div>
        <div class="btn">${svg("gear")}</div><div class="btn themebtn">${svg("moon")}</div><div class="btn">${svg("info")}</div>
      </div>
      <div class="crumbs"><div class="crumbBar" style="display:flex;align-items:center;gap:2px"></div><div class="legend"></div><div class="info"></div><div class="btn outline filterbtn">${svg("funnel")}<span class="ft">Filter</span></div></div>
      <div class="rail" hidden></div>
      <div class="body">
        <aside class="side">
          <h2>Drives</h2><div class="sub">Click a drive to scan it.</div><div class="drives"></div><div class="rule"></div>
          <h2 style="margin-top:4px">Largest items</h2>
          <div class="tabs"><span class="tab on">Files</span><span class="tab">Folders</span><span class="tab">Types</span><span class="tab tabChanges" hidden>Changes</span></div>
          <div class="sub listSub">Largest files in C:\\</div><div class="rows"></div>
        </aside>
        <div class="mapcol">
          <div class="map"><canvas></canvas><div class="start" hidden></div><div class="drawer" hidden></div><div class="sample-tag">Mockup with sample data · not the app, but close</div></div>
        </div>
      </div>
      <div class="status"><div class="hover"></div><div class="scan"></div></div>
      <div class="bottom"></div>
      <div class="ov tip" hidden></div><div class="ov cmenu" hidden></div>
      <div class="scrim" hidden><div class="dialog"></div></div>
    </div>`;
    const win = host.querySelector(".ssm");
    const $ = (sel) => win.querySelector(sel);
    const canvas = $(".map canvas"), ctx = canvas.getContext("2d"), map = $(".map");
    let items = [], base = null;

    // ---- side panel
    const DRIVES = [{ name: "C:  Local Disk", used: 271.37, total: 1024, fs: "NTFS" }, { name: "D:  Data", used: 1560, total: 1863, fs: "NTFS" }, { name: "E:  Games", used: 3120, total: 3726, fs: "NTFS" }, { name: "F:  Backup", used: 2210, total: 3726, fs: "exFAT" }, { name: "G:  USB Drive", used: 12.4, total: 57.6, fs: "Removable" }];
    $(".drives").innerHTML = DRIVES.map(d => `<div class="drive">${svg("drive")}<span class="name">${d.name}</span><span class="free">${fmt((d.total - d.used) * GB)} free</span><div class="bar"><i style="width:${(100 * d.used / d.total).toFixed(1)}%"></i></div><div class="detail">${fmt(d.used * GB)} used of ${fmt(d.total * GB)} · ${d.fs}</div></div>`).join("");
    function fillSideList(changes) {
      win.querySelectorAll(".side .tab").forEach(t => t.classList.toggle("on", changes ? t.classList.contains("tabChanges") : t.textContent === "Files"));
      if (!changes) {
        const top = files.slice(0, 7), largest = top[0].size;
        $(".listSub").textContent = "Largest files in C:\\";
        $(".rows").innerHTML = top.map(f => `<div class="row"><div><div class="n">${f.name}</div><div class="d">${f.parent.path}</div></div><div class="s">${fmt(f.size)}</div><div class="bar" style="width:${Math.round(292 * f.size / largest)}px"></div></div>`).join("");
        return;
      }
      const rows = files.map(f => ({ f, d: changeOf(f) })).filter(x => x.d !== 0).map(x => ({ ...x, abs: Math.abs(x.d ?? x.f.size) })).sort((a, b) => b.abs - a.abs).slice(0, 7);
      const largest = rows[0].abs;
      $(".listSub").textContent = "Biggest changes since yesterday";
      $(".rows").innerHTML = rows.map(({ f, d, abs }) => { const what = d === null ? "new" : d > 0 ? "grew" : "shrank", color = d === null ? "#F5B82E" : d > 0 ? "#E87A4F" : "#5DA9C9"; return `<div class="row chg"><span class="sw" style="background:${color}"></span><div><div class="n">${f.name}</div><div class="d">${what} · ${f.parent.path}</div></div><div class="s">${d === null || d > 0 ? "+" : "−"}${fmt(abs)}</div><div class="bar" style="width:${Math.round(292 * abs / largest)}px;left:24px"></div></div>`; }).join("");
    }

    // ---- map
    const live = { hovered: null, selected: null, multi: [], filter: null, zoomNode: null, peek: null };
    const matcher = () => live.peek || (live.filter ? live.filter.matches : null);
    function draw() {
      const w = map.clientWidth, h = map.clientHeight; if (!w || !h) return;
      canvas.style.width = w + "px"; canvas.style.height = h + "px";
      items = E.render(canvas, w, h, live.zoomNode || root, matcher(), state.group);
      base = document.createElement("canvas"); base.width = canvas.width; base.height = canvas.height; base.getContext("2d").drawImage(canvas, 0, 0);
      paintOverlay();
    }
    function paintOverlay() {
      if (!base) return;
      ctx.save(); ctx.setTransform(1, 0, 0, 1, 0, 0); ctx.drawImage(base, 0, 0); ctx.restore();
      const dpr = window.devicePixelRatio || 1; ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      const find = n => items.find(i => i.node === n);
      const hov = live.hovered && live.hovered !== live.selected ? find(live.hovered) : null;
      if (hov) { ctx.fillStyle = "rgba(255,255,255,.25)"; ctx.fillRect(hov.x, hov.y, hov.w, hov.h); ctx.strokeStyle = "rgba(0,0,0,.6)"; ctx.lineWidth = 4; ctx.strokeRect(hov.x + 1, hov.y + 1, hov.w - 2, hov.h - 2); ctx.strokeStyle = "#fff"; ctx.lineWidth = 2; ctx.strokeRect(hov.x + 1, hov.y + 1, hov.w - 2, hov.h - 2); }
      for (const n of [live.selected, ...live.multi]) { const it = n && find(n); if (it) { ctx.strokeStyle = "#F5B82E"; ctx.lineWidth = 3; ctx.strokeRect(it.x + 1.5, it.y + 1.5, it.w - 3, it.h - 3); } }
    }
    const nodeAt = (x, y) => { for (let i = items.length - 1; i >= 0; i--) { const it = items[i]; if (x >= it.x && y >= it.y && x < it.x + it.w && y < it.y + it.h) return it.node; } return null; };
    function showInfo(node) {
      const el = $(".status .hover"); if (!node) { el.textContent = ""; return; }
      let t = node.group ? `${node.name} too small to show in ${node.parent.path}    ${fmt(node.size)}    zoom in to see them` : `${node.path}    ${fmt(node.size)}`;
      if (node !== root) t += `  (${pct(node.size, root.size)} of C:\\)`;
      if (node.dir) t += `    ${num(node.files)} files`;
      el.textContent = t;
    }
    const localPoint = (e) => { const r = canvas.getBoundingClientRect(); return [(e.clientX - r.left) * map.clientWidth / r.width, (e.clientY - r.top) * map.clientHeight / r.height]; };
    if (state.interactive) {
      map.addEventListener("mousemove", e => { if (state.scene !== "none") return; const n = nodeAt(...localPoint(e)); if (n === live.hovered) return; live.hovered = n; paintOverlay(); showInfo(n || live.selected); });
      map.addEventListener("mouseleave", () => { if (state.scene !== "none") return; live.hovered = null; paintOverlay(); showInfo(live.selected); });
      map.addEventListener("click", e => { if (state.scene !== "none") return; live.selected = nodeAt(...localPoint(e)); paintOverlay(); showInfo(live.selected); });
    }

    // ---- the panel under the map
    function buildBottom(peekType, peekBand) {
      const el = $(".bottom");
      // The drawer needs the map's full height, so it folds the panel down to its titles while it is open.
      const mode = state.bottom === "on" && (state.scene === "drawer" || state.scene === "inspect") ? "collapsed" : state.bottom;
      el.hidden = mode === "off"; el.classList.toggle("collapsed", mode === "collapsed");
      if (el.hidden) return;
      if (state.scene === "start") {
        // Before a scan the panel is only its titles and one line saying what it will do.
        el.classList.add("empty");
        el.innerHTML = `<div class="bsec"><h3>By type</h3><div class="sum">Scan a drive or folder and this panel sums it up.</div></div><div class="vsep"></div><div class="bsec"><h3>Safe to clear</h3></div><div class="vsep"></div><div class="bsec"><h3>When changed</h3></div><div class="bchev">${svg("chev")}</div>`;
        return;
      }
      el.classList.remove("empty");
      const shares = typeShares(), total = root.size, order = shares.map((v, i) => [i, v]).sort((a, b) => (a[0] === 7) - (b[0] === 7) || b[1] - a[1]);
      const cols = E.cats();
      const top = order[0];
      const stack = `<div class="stack">${order.filter(([, v]) => v > 0).map(([i, v]) => `<i style="width:${(100 * v / total).toFixed(2)}%;background:${rgb(cols[i])};opacity:${peekType !== undefined && peekType !== i ? .35 : 1}">${v / total > .25 ? `${CATS[i]} · ${fmt(v)}` : ""}</i>`).join("")}</div>`;
      const legend = `<div class="tlegend">${order.slice(0, 8).map(([i, v]) => `<span class="${peekType === i ? "on" : ""}"><i style="background:${rgb(cols[i])}"></i>${CATS[i]}<span class="z">${fmt(v)} &nbsp;${pct(v, total)}</span></span>`).join("")}</div>`;
      const sumTop = order.slice(0, 3);
      const typeSum = peekType !== undefined ? `${CATS[peekType]}: ${fmt(shares[peekType])} in ${num(files.filter(f => category(f.name) === peekType).length)} files` : `${sumTop.map(([i]) => CATS[i].toLowerCase()).join(", ")} are ${pct(sumTop.reduce((s, [, v]) => s + v, 0), total)} of the scan. Hover a type to see it on the map, click to filter.`;
      const clean = cleanupRows(); const cleanTotal = clean.filter(r => r.action !== "How to").reduce((s, r) => s + r.size, 0);
      const cleanRows = clean.slice(0, 5).map(r => `<span class="nm">${r.name}</span><span class="z">${fmt(r.size)}</span><span class="lk">Show</span><span class="act">${r.action}</span>`).join("");
      const also = clean.slice(5).length ? `<div class="also">Also: ${clean.slice(5).map(r => `${r.name} (${fmtShort(r.size)})`).join(", ")}</div>` : "";
      const months = monthBuckets(), mx = Math.max(...months); const bands = bandSums();
      const bandCol = [[0xF5, 0xB8, 0x2E], [0xC4, 0x8F, 0x22], [0x8A, 0x65, 0x19], [0x4E, 0x5A, 0x68]];
      const bandOfMonth = (m) => m < 1 ? 0 : m < 12 ? 1 : m < 36 ? 2 : 3;
      const hist = `<div class="hist">${months.map((v, m) => `<i style="height:${Math.max(2, 100 * v / mx).toFixed(1)}%;background:${rgb(bandCol[bandOfMonth(m)])};opacity:${peekBand !== undefined && bandOfMonth(m) !== peekBand ? .3 : 1}"></i>`).reverse().join("")}</div>`;
      const bandNames = ["This month", "This year", "1 to 3 years", "Older"];
      const bandsHtml = `<div class="bands">${bands.map((v, i) => `<div class="band${peekBand === i ? " on" : ""}"><span class="nm"><i style="background:${rgb(bandCol[i])}"></i><span>${bandNames[i]}</span></span><b>${fmtShort(v)}</b></div>`).join("")}</div>`;
      const axis = `<div class="axis"><span>Oct 2023 and older</span><span>Oct 2026</span></div>`;
      el.innerHTML = `
        <div class="bsec"><h3>By type</h3><div class="sum">${typeSum}</div><div class="bbody">${stack}${legend}</div></div><div class="vsep"></div>
        <div class="bsec"><h3>Safe to clear</h3><div class="sum">${fmt(cleanTotal)} in places that are usually safe to empty. Nothing is deleted until you say so.</div><div class="bbody"><div class="clean">${cleanRows}</div>${also}</div></div><div class="vsep"></div>
        <div class="bsec"><h3>When changed</h3><div class="sum">${fmt(bands[2] + bands[3])} has not been touched in over a year. Hover a month to see it on the map, click to filter.</div><div class="bbody">${hist}${axis}${bandsHtml}</div></div>
        <div class="bchev">${svg(mode === "collapsed" ? "chevUp" : "chev")}</div>`;
    }

    // ---- filter faces
    const isVideo = n => !n.dir && !n.group && category(n.name) === 1;
    const containsMatch = (n, pred) => n.dir ? n.children.some(c => containsMatch(c, pred)) : n.group ? n.members.some(pred) : pred(n);
    const bigVideo = { text: "type:video >500MB", desc: "video, over 500 MB", matches(n) { const pred = f => isVideo(f) && f.size > 500 * MB; return containsMatch(n, pred); } };
    const largeFiles = { text: ">1GB is:file", matches(n) { return containsMatch(n, f => f.size > 1 * GB); } };
    const typeFilter = (c) => ({ matches(n) { return containsMatch(n, f => category(f.name) === c); } });
    const bandFilter = (b) => ({ matches(n) { return containsMatch(n, f => ageBand(f) === b); } });
    function buildDrawer(m, bytes) {
      const shares = typeShares(), total = root.size, cols = E.cats();
      const order = shares.map((v, i) => [i, v]).sort((a, b) => (a[0] === 7) - (b[0] === 7) || b[1] - a[1]).slice(0, 5);
      const rows = order.map(([i, v]) => `<div class="trow"><span class="cb"></span><i style="background:${rgb(cols[i])}"></i><span class="nm">${CATS[i]}</span><span class="bar"><b style="width:${(100 * v / total).toFixed(1)}%;background:${rgb(cols[i])}"></b></span><span class="z">${fmtShort(v)}</span></div>`).join("");
      $(".drawer").innerHTML = `
        <div class="dh"><b>Filter</b><span class="x">Clear all ${svg("x")}</span></div>
        <div class="dbody">
          <div class="sec"><div class="sh"><span>Type</span><span>share of scan</span></div>${rows}<div class="more">3 more…</div></div>
          <div class="sec"><div class="sh"><span>Size</span><span class="v">over 1 GB</span></div><div class="slider"><div class="tr"></div><div class="fl" style="left:60%;right:0"></div><div class="th" style="left:60%"></div><div class="th" style="left:100%"></div></div><div class="ticks"><span>any</span><span>10 MB</span><span>100 MB</span><span>1 GB</span><span>10 GB</span><span>any</span></div></div>
          <div class="sec"><div class="sh"><span>Not modified for</span></div><div class="seg"><span class="on">Any time</span><span>1 month</span><span>3 months</span><span>6 months</span><span>1 year</span><span>2 years</span><span>5 years</span></div></div>
          <div class="sec"><div class="sh"><span>Show</span></div><div class="seg three"><span>Files and folders</span><span class="on">Files only</span><span>Folders only</span></div></div>
          <div class="sec"><div class="sh"><span>Name</span></div><div class="railname" style="width:100%;border-radius:6px">Name or pattern, e.g. *.iso</div></div>
          <div class="sec"><div class="sh"><span>Try</span></div><div class="chips"><span class="chip on">Large files</span><span class="chip">Big videos</span><span class="chip">Installers &amp; archives</span><span class="chip">Untouched for 2 years</span><span class="chip">Big and old</span><span class="chip">Recently changed</span></div></div>
        </div>
        <div class="df"><b>${num(m)} files · ${fmt(bytes)}</b><span>match</span><div class="btn accent">${svg("select")}Select matches</div></div>`;
    }
    function buildRail(m, bytes) {
      const cols = E.cats();
      $(".rail").innerHTML = `
        <span class="pill on"><span class="k">Type</span><i style="background:${rgb(cols[1])}"></i>Video ${svg("chev", "chev")}</span>
        <span class="pill on"><span class="k">Size</span>over 500 MB ${svg("chev", "chev")}</span>
        <span class="pill on"><span class="k">Age</span>untouched 1 year ${svg("chev", "chev")}</span>
        <span class="pill">Show ${svg("chev", "chev")}</span>
        <span class="railname">Name or pattern, e.g. *.iso</span>
        <span class="pill">Try ${svg("chev", "chev")}</span>
        <span class="count">${num(m)} files · ${fmt(bytes)} match</span><div class="btn outline">${svg("select")}Select matches</div><div class="btn" style="color:var(--dim);height:28px">Clear</div>`;
    }

    // ---- scenes
    const largestFolder = [...root.children].filter(c => c.dir).sort((a, b) => b.children.length - a.children.length || b.size - a.size)[0];
    const focusFile = files.find(f => isVideo(f) && f.size > 500 * MB) || files[0];
    const sameFolder = files.filter(f => f.parent === focusFile.parent).slice(0, 3);
    const typeOf = n => { const m = /\.([^.]+)$/.exec(n.name); return m ? `${m[1].toUpperCase()} file (${CATS[category(n.name)].toLowerCase()})` : "File"; };
    // Overlays are positioned in the window's own pixels; the stage may be scaled, so go through unscaled offsets.
    const mapOrigin = () => { let x = 0, y = 0; for (let e = map; e && e !== win; e = e.offsetParent) { x += e.offsetLeft; y += e.offsetTop; } return [x, y]; };
    const place = (el, x, y) => { const [ox, oy] = mapOrigin(); el.style.left = Math.max(8, Math.min(state.w - el.offsetWidth - 8, ox + x)) + "px"; el.style.top = Math.max(8, Math.min(state.h - el.offsetHeight - 8, oy + y)) + "px"; };
    const menuRow = (icon, text, key, cls = "") => `<div class="mi ${cls}">${icon ? svg(icon) : '<span class="blank"></span>'}<span>${text}</span>${key ? `<span class="k">${key}</span>` : ""}</div>`;
    const combo = (v) => `<div class="combo" style="width:220px;height:30px">${v}${svg("chev", "chev")}</div>`;
    const dlg = () => $(".dialog");
    const dialogTitle = (t) => `<div class="dt"><span style="width:14px;height:14px;display:inline-block">${MARK}</span>${t}<span class="x">✕</span></div>`;

    function applyScene() {
      const tip = $(".tip"), menu = $(".cmenu"), scrim = $(".scrim"), drawer = $(".drawer"), rail = $(".rail"), start = $(".start");
      tip.hidden = menu.hidden = scrim.hidden = drawer.hidden = rail.hidden = start.hidden = true;
      live.hovered = live.selected = live.zoomNode = live.filter = live.peek = null; live.multi = [];
      const fb = $(".filterbtn"); fb.classList.remove("on"); $(".ft").textContent = "Filter";
      const info = $(".crumbs .info"); info.textContent = `${fmt(root.size)} · ${num(root.files)} files`;
      $(".crumbBar").innerHTML = `<div class="crumb current">${svg("drive")}C:\\</div>`;
      $(".zoomlabel").textContent = "100%";
      win.querySelectorAll(".toolbar .btn.nav").forEach(b => b.style.opacity = "");
      $(".status .hover").textContent = "";
      $(".status .scan").textContent = state.scene === "compare" ? `${num(root.files)} files · ${num(totalDirs)} folders · ${fmt(root.size)} · 2.1 s · file table  ·  compared with the scan from yesterday` : `${num(root.files)} files · ${num(totalDirs)} folders · ${fmt(root.size)} · 2.1 s · file table`;
      let peekType, peekBand;
      switch (state.scene) {
        case "zoomed":
          live.zoomNode = largestFolder;
          $(".crumbBar").innerHTML = `<div class="crumb">${svg("drive")}C:\\</div><span style="color:var(--dim)">›</span><div class="crumb current">${largestFolder.name}</div>`;
          info.textContent = `${fmt(largestFolder.size)} · ${num(largestFolder.files)} files · ${pct(largestFolder.size, root.size)} of C:\\`;
          $(".zoomlabel").textContent = Math.round(100 * root.size / largestFolder.size) + "%";
          win.querySelectorAll(".toolbar .btn.nav").forEach(b => b.style.opacity = "1");
          break;
        case "drawer": {
          live.filter = largeFiles; fb.classList.add("on"); $(".ft").innerHTML = "Filter · <b>2</b>";
          const m = files.filter(f => largeFiles.matches(f)), bytes = m.reduce((s, f) => s + f.size, 0);
          buildDrawer(m.length, bytes); drawer.hidden = false;
          break;
        }
        case "rail": {
          live.filter = bigVideo; fb.classList.add("on"); $(".ft").innerHTML = "Filter · <b>3</b>";
          const m = files.filter(f => bigVideo.matches(f)), bytes = m.reduce((s, f) => s + f.size, 0);
          buildRail(m.length, bytes); rail.hidden = false;
          break;
        }
        case "panel": peekType = 1; live.peek = typeFilter(1).matches; break;
        case "timeline": peekBand = 3; live.peek = bandFilter(3).matches; break;
        case "cleanup": { const w = findDir("Windows.old"); if (w) { live.multi = [w]; } break; }
        case "multi": live.multi = sameFolder; break;
        case "hover": live.hovered = focusFile; break;
        case "menu": live.selected = focusFile; break;
        case "inspect": live.selected = largestFolder; break;
      }
      if (state.scene === "panel" || state.scene === "cleanup" || state.scene === "timeline") { if (state.bottom === "off") state.bottom = "on"; }
      buildBottom(peekType, peekBand);
      draw();
      const find = n => items.find(i => i.node === n);
      if (state.scene === "hover") {
        const it = find(focusFile); if (it) {
          tip.innerHTML = `<div class="t"><div class="n">${focusFile.name}</div><div class="z">${fmt(focusFile.size)}</div></div><div class="l">${typeOf(focusFile)} · <b>${pct(focusFile.size, focusFile.parent.size)}</b> of ${focusFile.parent.name}</div><div class="l">Modified ${agoText(focusFile)}</div>`;
          tip.hidden = false; place(tip, it.x + it.w * .55 + 16, it.y + it.h * .5 + 20); showInfo(focusFile);
        }
      }
      if (state.scene === "menu") {
        const it = find(focusFile); if (it) {
          const ext = /\.[^.]+$/.exec(focusFile.name)?.[0] || ".mp4";
          menu.innerHTML = `<div class="mi primary">${svg("info")}<span><b>Inspect</b><span class="sub">${focusFile.name}  ·  ${fmt(focusFile.size)}  ·  ${ext.slice(1).toUpperCase()} file</span></span><span class="k">Ctrl+I</span></div>` + '<div class="sep"></div>' + menuRow("zoomin", "Zoom to folder", "Double-click") + menuRow("up", "Up one folder", "Backspace", "off") + menuRow("home", "Show whole map", "Home", "off")
            + '<div class="sep"></div>' + menuRow("open", "Open", "Enter", "on") + menuRow("folderOpen", "Show in Explorer") + menuRow("props", "Properties", "Alt+Enter")
            + '<div class="sep"></div>' + menuRow("copy", "Copy path", "Ctrl+C") + menuRow("copy", "Copy name") + menuRow("funnel", `Show only *${ext} files`) + menuRow("select", `Select everything in ${focusFile.parent.name}`)
            + '<div class="sep"></div>' + menuRow("refresh", `Rescan ${focusFile.parent.name}`) + menuRow("file", "Scan file", "›") + menuRow("open", "Export to CSV", "›")
            + `<div class="foot">${menuRow("trash", "Move to Recycle Bin", "Del")}</div>`;
          menu.hidden = false; place(menu, it.x + it.w * .5, it.y + it.h * .45); showInfo(focusFile);
        }
      }
      if (state.scene === "multi") $(".status .hover").textContent = `${sameFolder.length} items selected · ${fmt(sameFolder.reduce((s, f) => s + f.size, 0))} · ${sameFolder.length} files    Del moves them to the Recycle Bin`;
      if (state.scene === "cleanup") { const w = findDir("Windows.old"); if (w) showInfo(w); }
      if (state.scene === "start") {
        start.innerHTML = `<div class="start-center"><div><div class="start-head"><span class="start-mark">${MARK}</span><div><div class="start-title">Pick a drive on the left, or pick up where you left off</div><div class="start-sub">Saved scans open instantly and are compared with the new one.</div></div></div>
          <div class="start-card"><div class="start-row">${svg("drive")}<span class="n">C:</span><span class="w">yesterday</span><span class="s">271.37 GB</span><span class="c">›</span></div><div class="start-row">${svg("drive")}<span class="n">E:</span><span class="w">4 days ago</span><span class="s">3.05 TB</span><span class="c">›</span></div><div class="start-row">${svg("folder")}<span class="n">C:\\Users\\User\\Downloads</span><span class="w">2 weeks ago</span><span class="s">14.44 GB</span><span class="c">›</span></div></div>
          <div class="start-actions"><div class="btn">${svg("folder")}Scan a folder</div><div class="btn">${svg("file")}Open a saved scan…</div></div></div></div>`;
        start.hidden = false; $(".status .scan").textContent = "Opening the last scan…";
        $(".crumbBar").innerHTML = `<span style="color:var(--dim);padding:0 6px">No scan yet</span>`; info.textContent = ""; $(".zoomlabel").textContent = "—";
        $(".side .rows").innerHTML = ""; $(".listSub").textContent = "Scan a drive or folder to see the largest items.";
      }
      if (state.scene === "about") {
        const rows = [["info", "Version " + state.version, "Installed. Updates arrive on their own; the newest version is on GitHub.", false], ["file", "What's new", "Changes in this version", true], ["keyboard", "Shortcuts", "Every key and mouse action", true], ["bug", "Report a bug", "Opens GitHub with your version and system filled in", true], ["idea", "Suggest a feature", "", true], ["globe", "Source on GitHub", "MIT license", true], ["book", "Credits", "Treemap algorithm, SpaceMonger, icons", true]];
        dlg().className = "dialog about";
        dlg().innerHTML = dialogTitle("About SpaceSharp") + `<div class="about-head">${MARK}<div><div class="t">SpaceSharp</div><div class="s">See where your disk space went.</div></div></div>
          <div class="about-list">${rows.map(([ic, t, sub, chev]) => `<div class="about-row">${svg(ic)}<div><div>${t}</div>${sub ? `<div class="sub">${sub}</div>` : ""}</div>${chev ? '<span class="c">›</span>' : ""}</div>`).join("")}</div>
          <div class="about-copy">© 2026 ClearanceClarence  ·  .NET 10 on Windows 11 (x64)</div>
          <div class="df"><div class="btn">${svg("copy")}Copy version info</div><span class="spacer"></span><div class="btn accent">Close</div></div>`;
        scrim.hidden = false;
      }
      if (state.scene === "update" || state.scene === "whatsnew") {
        const next = state.version, cur = "1.6.1";
        dlg().className = "dialog " + (state.scene === "update" ? "update" : "notes");
        dlg().innerHTML = state.scene === "update"
          ? dialogTitle("Update available") + `<div class="ucol"><span class="tile">${MARK}</span><div class="t">SpaceSharp ${next} is ready</div><div class="h">You have ${cur}</div>
            <div class="jump"><span class="from">${cur}</span>${svg("arrow")}<span class="to">${next}</span></div>
            <p>One look, shaded your way · The filter, redone · A panel under the map. Installing takes about a minute and restarts SpaceSharp. Your settings and saved scans are kept.</p>
            <div class="btn accent big">Install and restart</div><div class="btn outline wide">Remind me later</div><a>What's new in 2.0</a></div>`
          : dialogTitle(`What's new in ${next}`) + `<div class="nb"><b>ONE LOOK, SHADED YOUR WAY</b>\n•  One map: folder frames with title bars, cushioned files, a one-pixel grid. Shading, light, text and title bars are yours to set, with a live preview.\n\n<b>THE FILTER, REDONE</b>\n•  A drawer over the map with types, size, age and presets; or a rail of pills under the path.\n\n<b>A PANEL UNDER THE MAP</b>\n•  By type, Safe to clear, When changed.\n\n<b>PALETTES</b>\n•  Fifteen, twelve hues each, split by theme.\n…</div><div class="df"><div class="btn">${svg("open")}See all changes on GitHub</div><span class="spacer"></span><div class="btn accent">OK</div></div>`;
        scrim.hidden = false;
      }
      if (state.scene === "settings") {
        const row = (t, d, c) => `<div class="st-row"><div><div class="t">${t}</div><div class="d">${d}</div></div>${c}</div>`;
        const slider = (pctv, a, b) => `<div class="st-slider"><div class="tr"><i style="width:${pctv}%"></i><b style="left:${pctv}%"></b></div><div class="ends"><span>${a}</span><span>${b}</span></div></div>`;
        dlg().className = "dialog settings";
        dlg().innerHTML = dialogTitle("Settings") + `<div class="st-tabs"><span>General</span><span>Appearance</span><span class="on">Treemap</span><span>Map</span><span>Scanning</span></div>
          <div class="st-preview"><canvas></canvas></div>
          <div class="st-body"><div class="st-section">Shading</div><div class="st-note">How files are shaded inside their folders. The preview above follows every change.</div>
            <div class="st-card">
              ${row("Cushion shading", "Light-to-dark shading on files. Off is flat color.", `<span class="sw${state.shading === "Off" ? " off" : ""}"></span>`)}
              ${row("Presets", "Starting points for the sliders below.", `<div class="st-chips">${["Soft", "Classic", "Deep"].map(p => `<span class="${state.shading === p ? "on" : ""}">${p}</span>`).join("")}</div>`)}
              ${row("Depth", "How pronounced the shading is.", slider(SHADING[state.shading] ? SHADING[state.shading][0] : 55, "Flat", "Strong"))}
              ${row("Spread", "How far the light reaches before the shadow.", slider(SHADING[state.shading] ? SHADING[state.shading][1] : 45, "Short", "Long"))}
              ${row("Fade inside folders", "How much shading nested files keep.", slider(SHADING[state.shading] ? SHADING[state.shading][3] : 70, "Fades", "Keeps"))}
            </div></div>
          <div class="df"><div class="btn">${svg("refresh")}Reset to defaults</div><span class="spacer"></span><div class="btn accent">Close</div></div>`;
        scrim.hidden = false;
        const pc = dlg().querySelector(".st-preview canvas"); const pw = dlg().querySelector(".st-preview");
        requestAnimationFrame(() => { E.render(pc, pw.clientWidth, pw.clientHeight, largestFolder, null, true); pc.style.width = "100%"; pc.style.height = "100%"; });
      }
      if (state.scene === "inspect") buildInspect(scrim);
    }

    function buildInspect(scrim) {
      const f = largestFolder;
      const cands = []; (function walk(n) { for (const c of n.children) { if (c.dir) { walk(c); const big = Math.max(...c.children.map(k => k.size), 0); if (big >= c.size * .9) continue; } cands.push(c); } })(f);
      const isAnc = (a, b) => { for (let n = b.parent; n; n = n.parent) if (n === a) return true; return false; };
      const rows = []; for (const c of cands.sort((a, b) => b.size - a.size)) { if (rows.some(r => isAnc(r, c) || isAnc(c, r))) continue; rows.push(c); if (rows.length === 5) break; }
      const rel = n => { const parts = []; for (let x = n; x && x !== f; x = x.parent) parts.unshift(x.name); return parts.join("\\"); };
      const types = {}; let nf = 0, nd = 0; const sizes = []; const ages = [0, 0, 0, 0];
      (function walk(n) { for (const c of n.children) { if (c.dir) { nd++; walk(c); } else { nf++; sizes.push(c.size); types[category(c.name)] = (types[category(c.name)] || 0) + c.size; ages[ageBand(c)] += c.size; } } })(f);
      sizes.sort((a, b) => a - b); const median = sizes.length ? sizes[sizes.length >> 1] : 0;
      const typeRows = Object.entries(types).map(([k, v]) => [+k, v]).sort((a, b) => (a[0] === 7) - (b[0] === 7) || b[1] - a[1]);
      const cols = E.cats(); const ageCol = [hex("#F5B82E"), hex("#C48F22"), hex("#8A6519"), hex("#4E5A68")];
      const donut = (parts, center, label) => { let off = 25; return `<svg viewBox="0 0 42 42">${parts.map(([v, col]) => { const len = Math.min(99.99, 100 * v / f.size); const o = off; off -= len; return `<circle cx="21" cy="21" r="15.9" fill="none" stroke="${rgb(col)}" stroke-width="7" stroke-dasharray="${Math.max(0, len - 1.5)} ${100 - Math.max(0, len - 1.5)}" stroke-dashoffset="${o - .75}"/>`; }).join("")}<text x="21" y="20.5" text-anchor="middle" font-size="5.5" font-weight="600" fill="currentColor">${center}</text><text x="21" y="26" text-anchor="middle" font-size="3.6" fill="var(--dim)">${label}</text></svg>`; };
      const legend = r => r.map(([n, v, col]) => `<div class="lg"><i style="background:${rgb(col)}"></i><span>${n}</span><span class="p">${pct(v, f.size)}</span></div>`).join("");
      const ageNames = ["This month", "This year", "1 to 3 years", "Older"]; const old = ages[2] + ages[3];
      const top = rows[0]; const dominant = top && top.size >= f.size * .4;
      const siblings = f.parent.children.filter(c => c.dir).sort((a, b) => b.size - a.size); const rank = siblings.indexOf(f) + 1;
      const countFiles = n => n.dir ? n.children.reduce((a, c) => a + countFiles(c), 0) : 1;
      dlg().className = "dialog inspect";
      dlg().innerHTML = dialogTitle(`Inspect: ${f.path}`) + `
        <div class="ih"><div class="bc"><a>C:\\</a><span>›</span><span>${f.name}</span></div>
          <div class="nm2">${svg("folder", "g")}<div><div class="n">${f.name}</div><div class="k">Folder · ${num(nf)} files, ${num(nd)} folders</div></div><div class="z">${fmt(f.size)}</div></div>
          <div class="ctx">${svg("pie")}<div><b>${pct(f.size, root.size)}</b> of C:\\, ${rank === 1 ? "the largest of its " + siblings.length + " folders" : "number " + rank + " of " + siblings.length + " folders by size"}</div>
            ${svg("file")}<div>Mostly <b>${CATS[typeRows[0][0]].toLowerCase()}</b> (${fmt(typeRows[0][1])})${typeRows[1] ? ` and ${CATS[typeRows[1][0]].toLowerCase()} (${fmt(typeRows[1][1])})` : ""} <span class="d">· median file ${fmt(median)}</span></div>
            ${svg("clock")}<div>Last changed <b>${agoText(rows[0] || f.children[0])}</b>, ${pct(old, f.size)} untouched for over a year <span class="d">· grew ${fmt(Math.round(f.size * .06))} since the last scan</span></div></div></div>
        <div class="ib">${dominant ? `<div class="callout"><b>${pct(top.size, f.size)}</b> of this folder is one thing: ${rel(top)} at ${fmt(top.size)}.</div>` : ""}
          <div class="charts"><div><h6>By type</h6><div class="donut">${donut(typeRows.map(([k, v]) => [v, cols[k]]), fmt(typeRows[0][1]), CATS[typeRows[0][0]])}<div>${legend(typeRows.map(([k, v]) => [CATS[k], v, cols[k]]))}</div></div></div>
            <div><h6>By age</h6><div class="donut">${donut(ages.map((v, i) => [v, ageCol[i]]), fmt(old), "over a year old")}<div>${legend(ages.map((v, i) => [ageNames[i], v, ageCol[i]]))}</div></div></div></div>
          <h6>Where the space is</h6>
          ${rows.map(r => `<div class="wr"><div class="nmx">${svg(r.dir ? "folder" : "file")}<span>${rel(r)}${r.dir ? "\\" : ""}</span></div><div class="bar"><i style="width:${(100 * r.size / f.size).toFixed(1)}%"></i></div><span class="s">${fmt(r.size)}</span><span class="p">${pct(r.size, f.size)}</span></div>`).join("")}
          <div class="rem">These ${rows.length} are ${pct(rows.reduce((a, r) => a + r.size, 0), f.size)} of the folder. The other ${num(nf - rows.reduce((a, r) => a + (r.dir ? countFiles(r) : 1), 0))} files share ${fmt(f.size - rows.reduce((a, r) => a + r.size, 0))}.</div></div>
        <div class="df"><div class="btn">${svg("copy")}Copy details</div><div class="btn">${svg("folderOpen")}Show in Explorer</div><div class="btn">${svg("props")}Properties</div><span class="spacer"></span><div class="btn accent">Close</div></div>`;
      scrim.hidden = false;
    }

    // ---- chrome
    function applyChrome() {
      win.classList.toggle("light", state.theme === "light");
      win.style.width = state.w + "px"; win.style.height = state.h + "px";
      $(".title").textContent = `SpaceSharp ${state.version}`;
      $(".themebtn").innerHTML = svg(state.theme === "light" ? "sun" : "moon");
      if (!PALETTES[state.palette] || PALETTES[state.palette].theme !== state.theme) state.palette = state.theme === "dark" ? "Graphite" : "Pastel";
      const p = E.pal();
      $(".swatches").innerHTML = [0, 1, 2, 3, 4, 5, 6, 7].map(d => `<i style="background:${rgb(p.folder(d))}"></i>`).join("");
      $(".paletteText").textContent = state.palette;
      if (state.scene === "compare") state.mode = "change"; else if (state.mode === "change") state.mode = "branch";
      $(".modeText").textContent = Object.fromEntries(MODES)[state.mode];
      const legend = $(".legend"); legend.classList.toggle("show", state.mode === "type" || state.mode === "change");
      legend.innerHTML = state.mode === "change" ? [["Grew", "#E87A4F"], ["Shrank", "#5DA9C9"], ["Same", "#6A6A74"], ["New", "#F5B82E"]].map(([n, c]) => `<span><i style="background:${c}"></i>${n}</span>`).join("") : CATS.map((n, i) => `<span><i style="background:${rgb(E.cats()[i])}"></i>${n}</span>`).join("");
      $(".tabChanges").hidden = state.mode !== "change";
      fillSideList(state.mode === "change");
      $(".side").classList.toggle("hidden", !state.side);
      $(".sidebtn").classList.toggle("accent", state.side); $(".sidebtn").classList.toggle("only", state.side);
      $(".botbtn").classList.toggle("accent", state.bottom !== "off"); $(".botbtn").classList.toggle("only", state.bottom !== "off");
      $(".sample-tag").hidden = !state.tag;
      applyScene();
    }
    applyChrome();
    if (document.fonts && document.fonts.ready) document.fonts.ready.then(applyScene);

    return {
      state, scenes: SCENES, modes: MODES, shadings: Object.keys(SHADING),
      palettes: (theme) => Object.keys(PALETTES).filter(n => !theme || PALETTES[n].theme === theme),
      set(key, value) { state[key] = value; applyChrome(); },
      redraw: applyChrome,
      element: win
    };
  }

  window.SpaceSharpMock = { mount, scenes: SCENES, modes: MODES, palettes: (theme) => Object.keys(PALETTES).filter(n => !theme || PALETTES[n].theme === theme), shadings: Object.keys(SHADING) };
})();
