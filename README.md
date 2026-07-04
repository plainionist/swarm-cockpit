# swarm-cockpit

Sidecar web cockpit for SwarmForge.

## Quick Start (WSL/bash)

```bash
bash ./swarm-cockpit start
```

The service listens on all network interfaces at fixed port 5959.
From another machine on the same network, open:

```text
http://<host-ip>:5959
```

In another shell, start SwarmForge normally:

```bash
./swarm
```

Mirror each agent's live terminal screen from tmux into the cockpit:

```bash
bash ./swarm-cockpit enable-logs
```

This polls `tmux capture-pane` and shows the agent's actual rendered screen in the
web page (what you'd see in the terminal), instead of raw redraw bytes.

### Reply to an agent from the browser

Each agent panel has an input box. Type a reply and press Enter (or click Send) and
the text is delivered straight into that agent's tmux pane via `tmux send-keys` — so
you can answer the questions you see on the mirrored screen from any device on the
network, no agent cooperation required. Input is queued in the cockpit and delivered
by the same poller that mirrors the screen (within ~1s).

Disable screen mirroring:

```bash
bash ./swarm-cockpit disable-logs
```

## Notes

- Keep SwarmForge launch/config unchanged.
- No Copilot plugin/hook required.
- Allow inbound TCP 5959 on the host firewall if remote machines cannot connect.
