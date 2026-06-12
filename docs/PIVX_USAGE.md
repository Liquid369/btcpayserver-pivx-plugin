# PIVX Plugin Quick Start Guide

## Overview

This plugin enables PIVX support in BTCPay Server using PIVX's integrated daemon
(pivxd). A single daemon handles everything: full node, wallet and RPC.

## Setup

### 1. PIVX daemon configuration

**`~/.pivx/pivx.conf`:**
```conf
server=1
rpcuser=rpc
rpcpassword=your_secure_password
rpcallowip=172.17.0.1   # Docker bridge IP (the address BTCPay connects from)
rpcport=51473
```

Start `pivxd` and let it sync.

### 2. BTCPay Server environment variables

Add to your BTCPay Server `.env` file or docker-compose environment:

```bash
BTCPAY_PIVX_DAEMON_URI=http://172.17.0.1:51473
BTCPAY_PIVX_RPCUSER=rpc
BTCPAY_PIVX_RPCPASSWORD=your_secure_password
# Optional: confirmation floor. 0 (default) = follow the store's speed policy.
BTCPAY_PIVX_MINCONF=0
```

After adding, regenerate and restart:
```bash
cd ~/btcpay/btcpayserver-docker
. btcpay-setup.sh -i
./btcpay-down.sh
./btcpay-up.sh
```

### 3. Install the plugin

1. Go to **Server Settings → Plugins**
2. Click **Upload Plugin**, select `BTCPayServer.Plugins.PIVX.btcpay`
3. Restart BTCPay Server

### 4. Verify installation

```bash
docker logs generated_btcpayserver_1 | grep -i pivx
```

You should see the PIVX service start and report the configured daemon URI.

## Enable PIVX in your store

1. Go to **Store Settings** — **PIVX** appears in the sidebar (Plugins section)
2. Verify the daemon connection shows green with chain/block info
3. Check **"Enable PIVX payments for this store"**
4. Optionally check **"Use shielded (SHIELD/Sapling) addresses"** to give customers
   private ps… addresses instead of transparent D… addresses
5. Click **Save**

## Payment flow

Once enabled:

1. Every new invoice gets a fresh PIVX address from the daemon wallet
2. The checkout page shows a PIVX tab with the address, amount and QR code
   (`pivx:<address>?amount=<due>` link)
3. The background service polls the daemon every 10 seconds and registers payments
   on the invoice — partial payments and overpayments are tracked correctly
4. The invoice settles when payments reach the required confirmations
   (store speed policy: High=0, Medium=1, LowMedium=2, Low=6)

## Verifying payment detection manually

```bash
# Watch the plugin register payments
docker logs -f generated_btcpayserver_1 | grep "registered PIVX payment"

# Check an address from the daemon side
pivx-cli listunspent 0 9999999 '["DYourInvoiceAddress"]'
pivx-cli listreceivedbyshieldaddress "psYourShieldAddress" 0
```

## Troubleshooting

**Plugin not loading** — check `docker logs generated_btcpayserver_1 | grep -i plugin`
for load errors; make sure the `.btcpay` was built by PluginPacker, not a manual zip.

**Daemon unreachable** — test from the BTCPay container:
```bash
docker exec generated_btcpayserver_1 curl --user rpc:your_secure_password \
  --data-binary '{"jsonrpc":"1.0","method":"getblockchaininfo"}' \
  http://172.17.0.1:51473/
```

**PIVX missing from checkout** — confirm the store has PIVX enabled and the invoice
was created after enabling; check the rate rule resolves (Store Settings → Rates →
test `PIVX_USD`).

**Payments not detected** — the address must belong to the daemon wallet that the
plugin talks to; verify with `pivx-cli listunspent`, and check the BTCPay logs for
"PIVX scan" RPC errors.

## Security notes

- All stores share the daemon wallet — don't use on a multi-tenant instance
- Back up `wallet.dat` (`pivx-cli backupwallet`); invoice funds live in the daemon
- Keep RPC restricted (`rpcallowip`) with a strong password
