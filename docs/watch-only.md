# Watch-only mode

By default the plugin generates invoice addresses from the daemon wallet, which
means spending keys live on the server. Watch-only mode lets a store keep keys
in its own wallet and give BTCPay only public key material. Enable it per store
under Store Settings > PIVX.

## Transparent

The store provides an account xpub. The plugin derives a child address per
invoice (m/44'/119'/account'/0/index, non-hardened on the last two levels),
registers it with the daemon via `importaddress` (no rescan needed for fresh
addresses), and detection works as today once `listunspent` is called with
`watchonly_config=2`.

One caveat: PIVX Core's own wallet derives with hardened steps on every level
(m/44'/119'/account'/change'/index'), so an xpub cannot reproduce addresses from
a PIVX Core wallet.dat, and there is no xpub export RPC anyway. Watch-only
works with wallets that use the common non-hardened BIP44 layout, such as
MyPIVXWallet or hardware wallets. The settings page should display the first
derived address so the owner can compare it against their wallet before going
live.

The derivation index has to be persisted per store and incremented atomically,
since invoice creation can run concurrently. Address gap limits also apply:
every invoice consumes an address whether or not it gets paid, so wallets with
a small gap limit may need it raised.

## Shielded

The store provides a Sapling extended full viewing key (`pxviews1...`, from
`exportsaplingviewingkey`). The key is imported once with
`importsaplingviewingkey` and a rescan from its birth height. After that, pivxd
detects notes sent to any diversified address of that key on its own; the
wallet trial-decrypts outputs with the incoming viewing key and registers
addresses as it finds them. Querying an address with
`listreceivedbyshieldaddress` before its first payment returns a "does not
belong to this node" error, which just means no payments yet.

pivxd has no RPC to derive diversified addresses from a viewing key, and .NET
has no Sapling cryptography, so derivation is delegated to
[pivx-walletd](https://github.com/Liquid369/pivx-walletd), a small stateless
service built on the same Rust stack as PIVX-Labs/pivx-shield. The plugin asks
it for the address at the store's next diversifier index:

```
POST /derive { "fvk": "pxviews1...", "index": 7 }
  -> { "address": "ps1...", "index": 9 }
```

Roughly half of diversifier indexes are invalid, so the service returns the
first valid index at or after the requested one, and the plugin stores that
index plus one as the next cursor.

## Configuration summary

Store settings: check "Watch-only", then provide an account xpub (transparent)
or a Sapling viewing key plus its birth height (shielded). The settings page
shows the first derived transparent address for comparison against the owner's
wallet.

Instance settings: shielded watch-only needs `BTCPAY_PIVX_WALLETD_URI` pointing
at a pivx-walletd instance. The docker fragment in this directory includes the
container; remove it if no store uses shielded watch-only.

The derivation cursors are stored in BTCPay's settings table per store
(`PIVX_WATCHONLY_T_{storeId}` and `PIVX_WATCHONLY_S_{storeId}`) and advance
under a lock so concurrent invoices cannot reuse an address.
