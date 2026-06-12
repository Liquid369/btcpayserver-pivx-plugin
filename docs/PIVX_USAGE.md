# Using the plugin

The plugin talks to a single pivxd daemon over RPC. The daemon is the full node
and the wallet at the same time, there are no other services to run.

## Daemon setup

`~/.pivx/pivx.conf`:

```conf
server=1
rpcuser=rpc
rpcpassword=your_secure_password
rpcallowip=172.17.0.1
rpcport=51473
staking=0
```

`rpcallowip` should match the address BTCPay connects from (the docker bridge in
a standard btcpayserver-docker install). Disable staking, this wallet receives
customer funds.

Point BTCPay at the daemon with environment variables:

```bash
BTCPAY_PIVX_DAEMON_URI=http://172.17.0.1:51473
BTCPAY_PIVX_RPCUSER=rpc
BTCPAY_PIVX_RPCPASSWORD=your_secure_password
BTCPAY_PIVX_MINCONF=0
```

`BTCPAY_PIVX_MINCONF` is an optional floor on confirmations. Leave it at 0 to
follow the store's transaction speed policy like other coins do.

## Enabling in a store

Open Store Settings, click PIVX in the sidebar, check that the daemon shows as
connected and synced, then enable payments and save. There is an option to hand
out shielded (Sapling) addresses instead of transparent ones.

Once enabled, every invoice gets a fresh address from the daemon wallet. The
checkout page shows the address with a QR code, and a background service polls
the daemon every 10 seconds to register payments. Partial payments and
overpayments are tracked like any other payment method, and invoices settle when
payments reach the confirmations required by the store's speed policy.

## Checking payment detection

```bash
docker logs -f generated_btcpayserver_1 | grep "registered PIVX payment"

pivx-cli listunspent 0 9999999 '["DInvoiceAddress"]'
pivx-cli listreceivedbyshieldaddress "psShieldAddress" 0
```

## Troubleshooting

If the plugin does not load, check `docker logs generated_btcpayserver_1` for
plugin errors. The `.btcpay` file must come from PluginPacker, a hand-zipped
publish folder will not load correctly.

If the daemon shows as unavailable, test the connection from inside the
container:

```bash
docker exec generated_btcpayserver_1 curl --user rpc:your_secure_password \
  --data-binary '{"jsonrpc":"1.0","method":"getblockchaininfo"}' \
  http://172.17.0.1:51473/
```

If PIVX is missing from checkout, make sure the invoice was created after
enabling the payment method, and that the rate rule resolves on the store's
rates page (test PIVX_USD there).

## Wallet care

All stores on the instance share the daemon wallet, so this plugin is not
suitable for shared/multi-tenant BTCPay instances. Back up the wallet with
`pivx-cli backupwallet` and keep a copy off the server. Funds can be swept to a
treasury wallet with `pivx-cli sendtoaddress` without breaking payment
detection.
