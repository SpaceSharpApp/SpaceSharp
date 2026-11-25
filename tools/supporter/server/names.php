<?php
// Prints the names for SpaceSharp.Core/Resources/Supporters.txt: every order that gave a name and was not refunded, oldest first.
// Command line only. Download the private folder's orders first, or run it on the server.
//   php names.php <private folder>

declare(strict_types=1);

if (PHP_SAPI !== 'cli') {
    http_response_code(404);
    exit;
}
require __DIR__ . '/lib.php';

if ($argc < 2) {
    fwrite(STDERR, "Usage: php names.php <private folder>\n");
    exit(1);
}

$records = array_filter(array_map('read_json', glob(rtrim($argv[1], '/\\') . '/orders/order-*.json') ?: []));
$named = array_filter($records, fn (array $record): bool => ($record['name'] ?? '') !== '' && !($record['refunded'] ?? false));
usort($named, fn (array $first, array $second): int => [$first['date'], $first['order']] <=> [$second['date'], $second['order']]);
foreach (array_unique(array_column($named, 'name')) as $name) {
    echo $name, "\n";
}
