<?php
// Makes a key by hand, for testing or for thanking someone outside the shop. Command line only.
//   php issue.php <private folder> "Name to show" [order]

declare(strict_types=1);

if (PHP_SAPI !== 'cli') {
    http_response_code(404);
    exit;
}
require __DIR__ . '/lib.php';

if ($argc < 3) {
    fwrite(STDERR, "Usage: php issue.php <private folder> \"Name to show\" [order]\n");
    exit(1);
}
$order = $argv[3] ?? 'manual-' . gmdate('Ymd-His');
echo make_key(tidy_name($argv[2]), $order, gmdate('Y-m-d'), rtrim($argv[1], '/\\') . '/supporter-private.pem'), "\n";
