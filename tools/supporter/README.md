# The supporter edition's other half

The app reads supporter keys (`SpaceSharp.Core/Services/SupporterKey.cs`). This folder holds what makes them: the script for the signing key pair, and the PHP that turns a paid Polar order into a key.

```
new-keys.ps1          makes the key pair, once; writes (or puts back) the public half in SupporterKey.cs
pack-server.ps1       puts together the folder to upload: the code below plus the private folder with the key
server/hook.php       Polar calls this for every order event; a paid order gets a key, saved and emailed
server/thanks.php     where the buyer lands after paying; shows the key
server/lib.php        shared code                server/where.php   where the private folder is
server/issue.php      a key by hand (command line)
server/names.php      the names for Supporters.txt (command line)
server/config.sample.php   the settings to fill in
```

Three things are secret and never go into the repository, a chat or an email: the private key (`supporter-private.pem`), the webhook secret, and `config.php` that holds it. `.gitignore` names the two files.

## The private folder

The key, the settings and the orders live in a folder named `private-<random>` inside `/supporter/` on the site, because the host gives no access outside the web root. Three things keep it private there. Every file in it is a `.php` file whose first line is `<?php exit; ?>` (or that only returns a value), so a server asked for one runs it and answers with an empty page; this holds on Apache and IIS alike. The folder has an `.htaccess` and a `web.config` that deny everything. And its name is random. On a host that lets you reach outside the web root, move the folder there and return its path from `where.php`.

## Set up, once

1. **The key pair.** In PowerShell, from the repository: `.\tools\supporter\new-keys.ps1`. It writes `supporter-private.pem` to `SpaceSharp-supporter` in your user folder and the public half into `SupporterKey.cs`. Put a copy of the private key in your password manager: if it is lost, no new keys can be made for the app versions already out. Commit the change to `SupporterKey.cs`; `dotnet test` fails until it is there. Running the script again never makes a new pair: it puts the public half back into `SupporterKey.cs` when that line is empty, and stops when the line holds a key that is not this private key's.
2. **Polar.** Create the organization and connect payouts. Make one product, a one-time purchase with pay-what-you-want pricing and a minimum of 5. Add an optional text custom field to its checkout with the slug `credit_name` and a label such as "Name to show in the credits (optional)". Create a checkout link with the success URL `https://spacesharp.app/supporter/thanks.php?checkout_id={CHECKOUT_ID}`. Do all of this in Polar's sandbox first; it has its own products, secrets and test cards.
3. **The upload folder.** `.\tools\supporter\pack-server.ps1` writes `SpaceSharp-supporter\upload\supporter` in your user folder: the code and the private folder. Open `config.php` in the private folder and fill in `product_id` (Polar: the product's ID).
4. **The webhook.** In Polar, add a webhook endpoint for `https://spacesharp.app/supporter/hook.php` with the events `order.paid` and `order.refunded`. Copy its secret into the same `config.php` as `webhook_secret`.
5. **Upload.** Upload the `supporter` folder to the site's root, so the addresses are `/supporter/hook.php` and `/supporter/thanks.php`. Upload only; do not synchronize with deleting, which would remove the orders on the server. Then open `https://spacesharp.app/supporter/hook.php` in a browser: an empty page (it answers 403) means PHP runs and the settings were found. Open `key.php` in the private folder the same way: it must be an empty page or an error, never text.
6. **Try it.** Buy the product in the sandbox with a test card. The thanks page should show a key within a few seconds, the email should arrive, and the key should be accepted in About › Supporter edition. Then repeat steps 2 and 4 in production, put the production values in `config.php` and upload it again.

If the email does not arrive, the order's file in the private folder's `orders` says `"emailed": false` when the server refused it. The sender in `config.php` has to be an address on the site's own domain.

## How a key is made

Polar signs each call (Standard Webhooks: HMAC-SHA256 over `id.timestamp.body`); `hook.php` answers 403 to anything that does not check, and ignores orders for other products. For a paid order it builds the note `{"n": name, "o": order, "d": date}`, signs `SSUP1.<note>` with the private key (ECDSA P-256, SHA-256, DER), writes `orders/order-<id>.php` and sends the email. An order that already has its file is left alone, so Polar repeating a call makes no second key.

A key cannot be taken back, because the app asks nobody. A refund marks the order's file, which keeps the name off the credits list.

## Crediting supporters

Before a release, download the private folder's `orders` into a folder of its own and print the names:

```
php tools/supporter/server/names.php <folder that holds orders/>
```

It lists every order that gave a name and was not refunded, oldest first. Read the list (it is free text typed by strangers), then paste it under the comment lines in `SpaceSharp.Core/Resources/Supporters.txt`. The Credits window shows that file.

## A key by hand

For testing, or to thank someone outside the shop:

```
php tools/supporter/server/issue.php <folder that holds supporter-private.pem> "Name to show"
```
