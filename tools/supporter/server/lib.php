<?php
// Shared by hook.php, thanks.php and the command-line scripts: the private folder, webhook checking, key signing, the order files.
//
// The private folder may have to sit inside the web root, where an address could reach it. So nothing in it is a plain file:
// every file there is a .php file that starts with GUARD, and a server asked for one runs it and sends back nothing.
// The folder's random name, its .htaccess and its web.config are the second and third locks, not the first.

declare(strict_types=1);

const KEY_PREFIX = 'SSUP1';
const GUARD = "<?php exit; ?>\n";

/** The private folder: config.php, key.php and orders/. where.php beside this file returns its path. */
function private_folder(): string
{
    $folder = require __DIR__ . '/where.php';
    if (!is_string($folder) || !is_file($folder . '/config.php')) {
        throw new RuntimeException('The private folder is not set up; see tools/supporter/README.md.');
    }
    return rtrim($folder, '/\\');
}

/** The settings from config.php in the private folder. */
function settings(string $folder): array
{
    return require $folder . '/config.php';
}

function base64url(string $bytes): string
{
    return rtrim(strtr(base64_encode($bytes), '+/', '-_'), '=');
}

/**
 * Checks a Standard Webhooks signature: HMAC-SHA256 over "id.timestamp.body", base64, sent as "v1,<signature>".
 * The key is the base64 after "whsec_". Polar secrets made before 8 September 2026 used the whole text as the key,
 * so that is tried too.
 */
function webhook_is_genuine(string $body, string $id, string $timestamp, string $signatures, string $secret): bool
{
    if ($id === '' || $secret === '' || !ctype_digit($timestamp) || abs(time() - (int) $timestamp) > 300) {
        return false;
    }

    $keys = [$secret];
    $decoded = base64_decode(preg_replace('/^whsec_/', '', $secret), true);
    if ($decoded !== false && $decoded !== '') {
        $keys[] = $decoded;
    }

    $signed = $id . '.' . $timestamp . '.' . $body;
    foreach (preg_split('/\s+/', trim($signatures)) as $candidate) {
        $sent = preg_replace('/^v1,/', '', $candidate);
        foreach ($keys as $key) {
            if (hash_equals(base64_encode(hash_hmac('sha256', $signed, $key, true)), $sent)) {
                return true;
            }
        }
    }
    return false;
}

/** The name as it will be shown: no control characters, single spaces, at most 60 characters. */
function tidy_name(mixed $name): string
{
    if (!is_string($name)) {
        return '';
    }
    $name = preg_replace('/[\p{C}\s]+/u', ' ', $name) ?? '';
    return mb_substr(trim($name), 0, 60);
}

/** The contents of a guarded file without its first line, or null when the file is missing or not guarded. */
function read_guarded(string $file): ?string
{
    $text = @file_get_contents($file);
    return is_string($text) && str_starts_with($text, GUARD) ? substr($text, strlen(GUARD)) : null;
}

/** The private key as PEM text: key.php on the server, or the plain supporter-private.pem on your own PC. */
function read_private_key(string $folder): string
{
    $pem = read_guarded($folder . '/key.php') ?? @file_get_contents($folder . '/supporter-private.pem');
    if (!is_string($pem) || $pem === '') {
        throw new RuntimeException('The private key is not in the private folder.');
    }
    return $pem;
}

/** Signs a key: "SSUP1.<note>.<signature>", where the note is JSON with n (name), o (order) and d (date). */
function make_key(string $name, string $order, string $date, string $privateKeyFolder): string
{
    $privateKey = openssl_pkey_get_private(read_private_key($privateKeyFolder));
    if ($privateKey === false) {
        throw new RuntimeException('The private key could not be read.');
    }

    $note = json_encode(['n' => $name, 'o' => $order, 'd' => $date], JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
    $signed = KEY_PREFIX . '.' . base64url($note);
    if (!openssl_sign($signed, $signature, $privateKey, OPENSSL_ALGO_SHA256)) {
        throw new RuntimeException('Signing failed.');
    }
    return $signed . '.' . base64url($signature);
}

/** An order or checkout number as a safe file name, or '' when it is not one. */
function file_id(mixed $id): string
{
    return is_string($id) && preg_match('/^[A-Za-z0-9_-]{6,80}$/', $id) ? $id : '';
}

function order_file(string $folder, string $order): string
{
    return $folder . '/orders/order-' . $order . '.php';
}

function checkout_file(string $folder, string $checkout): string
{
    return $folder . '/orders/checkout-' . $checkout . '.php';
}

function read_json(string $file): ?array
{
    $text = read_guarded($file);
    $value = $text === null ? null : json_decode($text, true);
    return is_array($value) ? $value : null;
}

function write_json(string $file, array $value): void
{
    if (!is_dir(dirname($file))) {
        mkdir(dirname($file), 0700, true);
    }
    file_put_contents($file, GUARD . json_encode($value, JSON_PRETTY_PRINT | JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES), LOCK_EX);
}

/** Sends the key by plain-text email. Returns whether the server accepted it for delivery. */
function send_key(array $settings, string $to, string $name, string $key): bool
{
    if (!filter_var($to, FILTER_VALIDATE_EMAIL)) {
        return false;
    }

    $greeting = $name === '' ? 'Hello,' : 'Hello ' . $name . ',';
    $text = implode("\r\n", [
        $greeting,
        '',
        'Thank you for supporting SpaceSharp. Here is your supporter key:',
        '',
        chunk_split($key, 64, "\r\n"),
        'To use it: open SpaceSharp, press F1 for About, choose Supporter edition and paste the key.',
        'It does not matter if the key arrives on several lines. The key is checked on your PC and never expires.',
        '',
        'Keep this email; it is how you get the key back on a new PC.',
        '',
        $settings['signature'],
    ]);
    $headers = [
        'From: ' . $settings['from'],
        'Reply-To: ' . $settings['reply_to'],
        'Content-Type: text/plain; charset=UTF-8',
    ];
    return mail($to, 'Your SpaceSharp supporter key', $text, implode("\r\n", $headers));
}
