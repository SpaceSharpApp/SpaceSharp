<?php
// Polar calls this address when something happens to an order. A paid order for the supporter product gets a signed key,
// which is kept in the private folder and emailed to the buyer. Answers 202 for everything genuine so Polar does not retry,
// and 403 for anything whose signature does not check.

declare(strict_types=1);

require __DIR__ . '/lib.php';

try {
    $folder = private_folder();
    $settings = settings($folder);
} catch (Throwable $problem) {
    error_log('SpaceSharp supporter hook: ' . $problem->getMessage());
    http_response_code(500);
    exit;
}

$body = file_get_contents('php://input') ?: '';
$genuine = webhook_is_genuine(
    $body,
    $_SERVER['HTTP_WEBHOOK_ID'] ?? '',
    $_SERVER['HTTP_WEBHOOK_TIMESTAMP'] ?? '',
    $_SERVER['HTTP_WEBHOOK_SIGNATURE'] ?? '',
    $settings['webhook_secret']
);
if (!$genuine) {
    http_response_code(403);
    exit;
}

http_response_code(202);
$event = json_decode($body, true);
$order = is_array($event) ? ($event['data'] ?? null) : null;
if (!is_array($order)) {
    exit;
}

$orderId = file_id($order['id'] ?? null);
$product = $order['product_id'] ?? ($order['product']['id'] ?? '');
if ($orderId === '' || $product !== $settings['product_id']) {
    exit;
}

$file = order_file($folder, $orderId);
$type = $event['type'] ?? '';

if ($type === 'order.refunded') {
    // A key cannot be taken back, since the app checks it without asking anyone. The mark keeps the name off the list.
    $record = read_json($file);
    if ($record !== null) {
        $record['refunded'] = true;
        write_json($file, $record);
    }
    exit;
}

// Polar sends an event again when it got no answer in time, so an order that already has its key is left alone.
if ($type !== 'order.paid' || is_file($file)) {
    exit;
}

try {
    $name = tidy_name($order['custom_field_data'][$settings['name_field']] ?? null);
    $date = gmdate('Y-m-d');
    $email = (string) ($order['customer']['email'] ?? '');
    $record = [
        'order' => $orderId,
        'date' => $date,
        'email' => $email,
        'name' => $name,
        'key' => make_key($name, $orderId, $date, $folder),
        'refunded' => false,
    ];
    write_json($file, $record);

    $checkoutId = file_id($order['checkout_id'] ?? null);
    if ($checkoutId !== '') {
        write_json(checkout_file($folder, $checkoutId), ['order' => $orderId]);
    }

    $record['emailed'] = send_key($settings, $email, $name, $record['key']);
    write_json($file, $record);
} catch (Throwable $problem) {
    // 500 makes Polar try again later; nothing was written for the order unless the key was made.
    error_log('SpaceSharp supporter hook: ' . $problem->getMessage());
    http_response_code(500);
}
