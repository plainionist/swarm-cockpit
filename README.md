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

Ask a blocking question from an agent shell:

```bash
bash ./swarm-cockpit ask \
  --agent Implementer \
  --context "Need decision" \
  --question "Use option A or B?" \
  --option "A" \
  --option "B" \
  --recommendation "A"
```

Answer in the web page. The waiting command continues.

Disable screen mirroring:

```bash
bash ./swarm-cockpit disable-logs
```

## Notes

- Keep SwarmForge launch/config unchanged.
- No Copilot plugin/hook required.
- Allow inbound TCP 5959 on the host firewall if remote machines cannot connect.
