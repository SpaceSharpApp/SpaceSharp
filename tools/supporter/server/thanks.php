<?php
// Where Polar sends the buyer after paying: https://spacesharp.app/supporter/thanks.php?checkout_id={CHECKOUT_ID}
// Shows the key as soon as hook.php has made it, so nobody has to wait for the email. The checkout number is a long random
// value only the buyer's browser was given, which is what makes showing the key here safe.

declare(strict_types=1);

require __DIR__ . '/lib.php';

header('Cache-Control: no-store');
header('X-Robots-Tag: noindex');

$record = null;
$attempt = max(0, min(20, (int) ($_GET['attempt'] ?? 0)));
$checkoutId = file_id($_GET['checkout_id'] ?? null);
try {
    $folder = private_folder();
    $pointer = $checkoutId === '' ? null : read_json(checkout_file($folder, $checkoutId));
    $record = $pointer === null ? null : read_json(order_file($folder, file_id($pointer['order'] ?? null)));
} catch (Throwable $problem) {
    error_log('SpaceSharp supporter thanks: ' . $problem->getMessage());
}

// The payment can reach this page a few seconds before Polar has told hook.php about it, so the page asks again for a while.
$waiting = $record === null && $checkoutId !== '' && $attempt < 20;
$again = '?checkout_id=' . rawurlencode($checkoutId) . '&attempt=' . ($attempt + 1);
$key = $record['key'] ?? '';
$name = $record['name'] ?? '';
?>
<!doctype html>
<html lang="en">
  <head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width,initial-scale=1" />
    <title>Thank you | SpaceSharp</title>
    <meta name="robots" content="noindex" />
    <meta name="theme-color" content="#0B0E13" />
    <meta name="color-scheme" content="dark" />
<?php if ($waiting): ?>
    <meta http-equiv="refresh" content="3;url=<?= htmlspecialchars($again) ?>" />
<?php endif; ?>
    <link rel="icon" href="/favicon.ico" sizes="32x32" />
    <link rel="stylesheet" href="/css/base.css" />
    <style>
      main {
        max-width: 720px;
        margin: 0 auto;
        padding: 72px 24px 96px;
      }
      .mark {
        display: flex;
        align-items: center;
        gap: 10px;
        font-family: var(--display);
        font-weight: 600;
        font-size: 20px;
        color: var(--ink);
        text-decoration: none;
      }
      h1 {
        font-family: var(--display);
        font-size: clamp(32px, 6vw, 44px);
        line-height: 1.1;
        margin: 48px 0 16px;
      }
      p,
      li {
        color: var(--muted);
        line-height: 1.6;
      }
      .key {
        display: block;
        width: 100%;
        box-sizing: border-box;
        margin: 28px 0 14px;
        padding: 18px;
        background: var(--panel);
        border: 1px solid var(--line-2);
        border-radius: var(--radius);
        color: var(--ink);
        font-family: var(--mono);
        font-size: 14px;
        line-height: 1.6;
        overflow-wrap: anywhere;
        user-select: all;
      }
      button {
        font: inherit;
        font-weight: 600;
        padding: 11px 20px;
        border: 0;
        border-radius: 8px;
        background: var(--amber);
        color: var(--amber-ink);
        cursor: pointer;
      }
      ol {
        margin: 36px 0 0;
        padding-left: 22px;
      }
      li + li {
        margin-top: 8px;
      }
      a {
        color: var(--amber-2);
      }
    </style>
  </head>
  <body>
    <main>
      <a class="mark" href="/"><img src="/icon.png" alt="" width="30" height="30" />SpaceSharp</a>
<?php if ($key !== ''): ?>
      <h1>Thank you<?= $name === '' ? '' : ', ' . htmlspecialchars($name) ?>.</h1>
      <p>This is your supporter key. It has also been sent to your email, so you can get it back on a new PC.</p>
      <code class="key" id="key"><?= htmlspecialchars($key) ?></code>
      <button type="button" id="copy">Copy the key</button>
      <ol>
        <li>Open SpaceSharp and press F1 for About.</li>
        <li>Choose Supporter edition.</li>
        <li>Paste the key and choose Use this key.</li>
      </ol>
      <script>
        document.getElementById("copy").addEventListener("click", function () {
          var button = this;
          navigator.clipboard.writeText(document.getElementById("key").textContent).then(function () {
            button.textContent = "Copied";
          });
        });
      </script>
<?php elseif ($waiting): ?>
      <h1>Thank you.</h1>
      <p>Your payment went through. The key is being made, and this page shows it in a few seconds.</p>
<?php else: ?>
      <h1>Thank you.</h1>
      <p>
        Your key is on its way to the email address you paid with. If it has not arrived within the hour, check the spam folder, then write to
        <a href="mailto:supporter@spacesharp.app">supporter@spacesharp.app</a> with the address you used.
      </p>
<?php endif; ?>
    </main>
  </body>
</html>
