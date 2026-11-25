<?php
// The private folder: config.php, supporter-private.pem and orders/. Keep it outside the web root, so no address reaches it.
// The default is a folder named supporter-private beside the web root.
return dirname($_SERVER['DOCUMENT_ROOT'] ?? __DIR__) . '/supporter-private';
