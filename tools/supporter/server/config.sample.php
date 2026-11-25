<?php
// Copy to config.php in the private folder and fill in. Never put config.php or the private key in the repository.
return [
    // Polar: Settings > Webhooks > the endpoint's secret (starts with whsec_ or polar_whs_).
    'webhook_secret' => '',
    // Polar: the supporter product's ID (Products > the product > Copy ID). Orders for anything else are ignored.
    'product_id' => '',
    // The slug of the checkout's custom text field that asks for the name to show.
    'name_field' => 'credit_name',
    // The sender of the key email. Use an address on the site's own domain or the email lands in spam.
    'from' => 'SpaceSharp <supporter@spacesharp.app>',
    'reply_to' => 'supporter@spacesharp.app',
    'signature' => 'SpaceSharp - https://spacesharp.app',
];
