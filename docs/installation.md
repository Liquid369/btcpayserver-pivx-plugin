# PIVX Plugin — Installation on BTCPayServer-docker (Ubuntu)

These instructions assume an existing [BTCPayServer-docker](https://docs.btcpayserver.org/Docker/)
deployment. The PIVX daemon runs natively on the host (there is no maintained
official PIVX docker image); the plugin talks to it over RPC.

## 1. Install pivxd on the host

```sh
# Download the official release (x86_64)
cd /tmp
wget https://github.com/PIVX-Project/PIVX/releases/download/v5.6.1/pivx-5.6.1-x86_64-linux-gnu.tar.gz
tar xzf pivx-5.6.1-x86_64-linux-gnu.tar.gz
sudo install -m 0755 pivx-5.6.1/bin/pivxd pivx-5.6.1/bin/pivx-cli /usr/local/bin/

# Dedicated user + data dir
sudo useradd -r -m -d /var/lib/pivxd -s /usr/sbin/nologin pivx
```

**`/var/lib/pivxd/.pivx/pivx.conf`** (as root, then `chown pivx:pivx` and `chmod 600`):

```conf
server=1
rpcuser=rpc
rpcpassword=CHANGE_ME_STRONG_PASSWORD
rpcport=51473
# Listen on all interfaces so docker containers can reach us,
# but only allow the private docker ranges:
rpcbind=0.0.0.0
rpcallowip=172.16.0.0/12
# This is a merchant hot wallet — don't stake with it
staking=0
```

**`/etc/systemd/system/pivxd.service`:**

```ini
[Unit]
Description=PIVX daemon
After=network-online.target
Wants=network-online.target

[Service]
User=pivx
Group=pivx
ExecStart=/usr/local/bin/pivxd -datadir=/var/lib/pivxd/.pivx -daemon=0
Restart=on-failure
TimeoutStopSec=120

[Install]
WantedBy=multi-user.target
```

```sh
sudo systemctl daemon-reload
sudo systemctl enable --now pivxd

# Block external access to the RPC port (docker bridge traffic doesn't pass ufw's INPUT chain for the host IP, but be explicit):
sudo ufw deny in on $(ip route | awk '/default/ {print $5; exit}') to any port 51473

# Watch it sync (compare blocks vs headers)
pivx-cli -datadir=/var/lib/pivxd/.pivx getblockchaininfo
```

Wait until the chain is fully synced before taking payments.

## 2. Add the custom fragment to BTCPayServer-docker

Copy [`pivx.custom.yml`](./pivx.custom.yml) to the server, edit the RPC password
to match `pivx.conf`, then:

```sh
sudo su -
cd /root/btcpayserver-docker   # or wherever btcpayserver-docker lives
cp /path/to/pivx.custom.yml docker-compose-generator/docker-fragments/pivx.custom.yml

export BTCPAYGEN_ADDITIONAL_FRAGMENTS="$BTCPAYGEN_ADDITIONAL_FRAGMENTS;pivx.custom"
. ./btcpay-setup.sh -i
```

`btcpay-setup.sh -i` persists `BTCPAYGEN_ADDITIONAL_FRAGMENTS`, regenerates
docker-compose and restarts the stack.

Verify the container sees the daemon:

```sh
docker exec generated_btcpayserver_1 curl -s --user rpc:CHANGE_ME_STRONG_PASSWORD \
  --data-binary '{"jsonrpc":"1.0","method":"getblockchaininfo"}' \
  http://host.docker.internal:51473/
```

## 3. Upload the plugin

1. Build/pack the plugin (or take a released `BTCPayServer.Plugins.PIVX.btcpay`):
   `Plugins/PIVX/bin/packed/BTCPayServer.Plugins.PIVX/<version>/BTCPayServer.Plugins.PIVX.btcpay`
2. In BTCPay: **Server Settings → Plugins → Upload Plugin**, select the `.btcpay` file
3. Restart BTCPay when prompted (or `docker restart generated_btcpayserver_1`)
4. Check the logs:
   ```sh
   docker logs generated_btcpayserver_1 2>&1 | grep -i pivx
   ```

## 4. Enable in your store

1. **Store Settings → PIVX** (sidebar)
2. Confirm the daemon shows connected/synced
3. Enable PIVX payments (optionally shielded addresses) and save
4. Create a small test invoice and pay it — see
   [PIVX_USAGE.md](./PIVX_USAGE.md) for the full testing checklist

## Wallet care

- The pivxd wallet (`/var/lib/pivxd/.pivx/wallet.dat`) holds all invoice funds —
  back it up: `pivx-cli -datadir=/var/lib/pivxd/.pivx backupwallet /root/pivx-wallet-backup.dat`
- All stores on the instance share this wallet; don't run this plugin on a
  multi-tenant BTCPay instance
