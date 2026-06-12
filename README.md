# PIVX Support Plugin

This plugin extends BTCPayServer to enable users to receive payments via PIVX (both transparent and shielded addresses).

> [!WARNING]
> This plugin shares a single PIVX wallet across all the stores in the BTCPayServer instance. Only use this plugin if you are not sharing your instance.

## Getting Started

### Installing the Plugin

[docs/installation.md](./docs/installation.md)

## Full Node

Running a full node with PIVX daemon

```sh
export BTCPAYGEN_EXCLUDE_FRAGMENTS="$BTCPAYGEN_EXCLUDE_FRAGMENTS;pivx"
export BTCPAYGEN_ADDITIONAL_FRAGMENTS="$BTCPAYGEN_ADDITIONAL_FRAGMENTS;pivx-fullnode"
. ./btcpay-setup.sh -i
```

You can create a local test build of the plugin manually using these steps:

### Cloning the Project

```sh
git clone --recurse-submodules https://github.com/Liquid369/btcpayserver-pivx-plugin
```

### Creating and Running a Local Build

> `cd` into the repository

```sh
cd btcpayserver
dotnet build .
cd ..
dotnet build .

cd btcpayserver/BTCPayServer
dotnet run
```

### Creating a Production Build Locally

```sh
dotnet publish Plugins/PIVX -c Release -o Plugins/PIVX/bin/publish
dotnet run --project btcpayserver/BTCPayServer.PluginPacker -- \
  Plugins/PIVX/bin/publish BTCPayServer.Plugins.PIVX Plugins/PIVX/bin/packed
```

The `.btcpay` package, manifest and SHA256SUMS are written to
`Plugins/PIVX/bin/packed/BTCPayServer.Plugins.PIVX/<version>/`.

## Contribution

You will need to create this file:

**`btcpayserver/BTCPayServer/appsettings.dev.json`**

```json
{
  "DEBUG_PLUGINS": "/<absolute-path-to-repo>/btcpayserver-pivx-plugin/Plugins/PIVX/bin/Debug/net8.0/BTCPayServer.Plugins.PIVX.dll",
  "PIVX": {
    "DaemonUri": "http://127.0.0.1:51473",
    "RpcUser": "rpc",
    "RpcPassword": "rpcpassword"
  }
}
```

(Alternatively set the `BTCPAY_PIVX_*` environment variables below.)

## Configuration

Configure this plugin using the following environment variables:

| Environment variable | Description | Example |
| --- |-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|---------|
**BTCPAY_PIVX_DAEMON_URI** | **Required**. The URI of the daemon RPC interface | http://127.0.0.1:51473 |
**BTCPAY_PIVX_RPCUSER** | RPC username for PIVX daemon | pivxuser |
**BTCPAY_PIVX_RPCPASSWORD** | RPC password for PIVX daemon | pivxpass |
**BTCPAY_PIVX_MINCONF** | Optional confirmation floor; 0 (default) follows the store's transaction speed policy | 0 |

## For Maintainers

If you are a developer maintaining this plugin, in order to maintain this plugin, you need to clone this repository with `--recurse-submodules`:

```sh
git clone --recurse-submodules https://github.com/Liquid369/btcpayserver-pivx-plugin
```

# Licence

[MIT](LICENSE.md)
