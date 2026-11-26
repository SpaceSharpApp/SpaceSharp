<?php
// The private folder: config.php, key.php and orders/. pack-server.ps1 makes it beside this file with a random name,
// private-<random>, for hosts where nothing outside the web root can be reached. On a host that lets you, move it out of
// the web root and return its path here instead.
return (glob(__DIR__ . '/private-*', GLOB_ONLYDIR) ?: [''])[0];
