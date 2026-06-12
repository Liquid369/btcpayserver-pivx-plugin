# Installation on btcpayserver-docker (Ubuntu)

These steps assume an existing [btcpayserver-docker](https://docs.btcpayserver.org/Docker/)
deployment. The PIVX daemon runs on the host, there is no maintained PIVX docker
image worth depending on.

## 1. Install pivxd

```sh
cd /tmp
wget https://github.com/PIVX-Project/PIVX/releases/download/v5.6.1/pivx-5.6.1-x86_64-linux-gnu.tar.gz
tar xzf pivx-5.6.1-x86_64-linux-gnu.tar.gz
sudo install -m 0755 pivx-5.6.1/bin/pivxd pivx-5.6.1/bin/pivx-cli /usr/local/bin/
sudo useradd -r -m -d /var/lib/pivxd -s /usr/sbin/nologin pivx
```

Create `/var/lib/pivxd/.pivx/pivx.conf` (owned by the pivx user, mode 600):

```conf
server=1
rpcuser=rpc
rpcpassword=your_secure_password
rpcport=51473
rpcbind=0.0.0.0
rpcallowip=172.16.0.0/12
staking=0
```

`rpcbind=0.0.0.0` with `rpcallowip` limited to the private docker ranges lets the
BTCPay container reach the daemon while refusing everything else. Add a firewall
rule for the public interface as well:

```sh
sudo ufw deny in on $(ip route | awk '/default/ {print $5; exit}') to any port 51473
```

A systemd unit, `/etc/systemd/system/pivxd.service`:

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
pivx-cli -datadir=/var/lib/pivxd/.pivx getblockchaininfo
```

Let the chain sync fully before taking payments.

## 2. Wire BTCPay to the daemon

Copy [pivx.custom.yml](./pivx.custom.yml) into the docker fragments directory,
edit the RPC password to match pivx.conf, and regenerate:

```sh
sudo su -
cd /root/btcpayserver-docker
cp /path/to/pivx.custom.yml docker-compose-generator/docker-fragments/pivx.custom.yml
export BTCPAYGEN_ADDITIONAL_FRAGMENTS="$BTCPAYGEN_ADDITIONAL_FRAGMENTS;pivx.custom"
. ./btcpay-setup.sh -i
```

The fragment uses `host.docker.internal` to reach the host. If your compose
generator strips the `extra_hosts` key, point `BTCPAY_PIVX_DAEMON_URI` at the
docker bridge IP (`http://172.17.0.1:51473`) instead.

Test from inside the container:

```sh
docker exec generated_btcpayserver_1 curl -s --user rpc:your_secure_password \
  --data-binary '{"jsonrpc":"1.0","method":"getblockchaininfo"}' \
  http://host.docker.internal:51473/
```

## 3. Upload the plugin

Build the package (see the README) or take a release `.btcpay`, then upload it
under Server Settings > Plugins > Upload Plugin and restart BTCPay. Check the
logs afterwards:

```sh
docker logs generated_btcpayserver_1 2>&1 | grep -i pivx
```

## 4. Enable in a store

Store Settings > PIVX, confirm the daemon connection, enable payments, save.
Create a small test invoice and pay it before going live.

The daemon wallet (`/var/lib/pivxd/.pivx/wallet.dat`) holds all invoice funds.
Back it up:

```sh
pivx-cli -datadir=/var/lib/pivxd/.pivx backupwallet /root/pivx-wallet-backup.dat
```
