# ZeroDay Panic — Emergency Infrastructure Containment

**Status:** Design proposal — not implemented  
**Proposed home:** `JoyfulReaper/MissionControl`  
**Suggested location:** `docs/ZERODAYPANIC.MD`  
**Drafted:** 2026-10-31  
**Initial scope:** User-owned Linux VPS fleet (Clanker, ScopeCreep, Wurstcase, FrontDesk), with a disposable test VM first

> **Core principle:** The hard part isn't cutting off a server. It's containing a threat without losing the ability to recover the server safely.

## 1. Idea and motivation

ZeroDay Panic is a **MissionControl emergency-containment feature** accessible from a phone or authenticated web browser. When a serious vulnerability, suspected compromise, or urgent exposure is discovered, the operator can apply a predefined network-containment policy to one or more VPSes without logging into every host individually.

The original idea was a single button that disables networking across the fleet until the operator uses each hosting provider's console/VNC to restore it. That behavior remains a possible **last-resort** mode, but the primary workflow should preserve a trusted recovery path so vulnerable services can be patched remotely.

**Goals:**

- Contain internet-facing services quickly from a phone while away from a workstation.
- Act on a single server or a selected set of servers, rather than automatically disabling everything.
- Reuse MissionControl's Dashboard, Mobile experience, host inventory, and existing Agents.
- Enforce only **predefined, host-specific** emergency policies; never offer arbitrary command execution.
- Keep the emergency action, status, audit record, and provider-console recovery process distinct.
- Support locally executable recovery even when MissionControl, public DNS, WireGuard, or the internet is unavailable.
- Avoid turning a compromised monitoring/dashboard session into a fleet-wide remote-root capability.

**Non-goals for the MVP:**

- Automatic vulnerability detection, scanning, patching, or infection cleanup.
- Replacing the existing firewall manager, WireGuard setup, BIRD, or normal backup services.
- A general remote shell, firewall rule editor, or generic scripting engine.
- Automatic restoration of networking following **full isolation**.
- Guaranteed simultaneous action on unreachable servers.
- A separately hosted web app, new monitoring database, or new always-running agent daemon.

## 2. Fit with MissionControl's existing architecture

Based on the MissionControl `dev` branch inspected on 2026-10-31:

- **`MissionControl.Dashboard`** is an authenticated ASP.NET Core / Blazor app and already owns server-side host fleet access.
- **`MissionControl.Mobile`** is a MAUI Blazor Hybrid app that calls authenticated Dashboard Mobile API endpoints, not Agents directly.
- **`MissionControl.Agent`** runs on nodes, gathers health/container/protocol data, persists the latest snapshot, and exposes `GET /api/snapshot`.
- Dashboard currently **pulls Agent snapshots directly**. Agents optionally **publish telemetry events** through Gateway and NATS JetStream.
- Agent snapshot endpoints do **not** currently provide application-level authentication; network controls protect that monitoring interface. Dashboard's Mobile API currently uses a bearer token.
- The Agent may have access to the Docker socket on certain deployments; **membership in the Docker group is effectively root-equivalent** and must be considered in any security model.

This is a natural extension to MissionControl's operational role, **but it must not turn the current monitoring endpoint into a privileged command endpoint**.

### Proposed component responsibilities

| Component | Responsibility |
| --- | --- |
| `MissionControl.Dashboard` | Authenticated operator workflow, reauthentication/step-up, command issuance, host selection, status, audit trail |
| `MissionControl.Mobile` / shared `MissionControl.UI` | Emergency page, confirmation and host status; web-first MVP can defer native Mobile UI work |
| `MissionControl.Contracts` | Explicit versioned containment-command, policy, acknowledgement, and status DTOs |
| `MissionControl.Client` | Typed clients and transport; no privileged secrets in Mobile |
| `MissionControl.Agent` | Validate host identity and command authenticity, report state, ask constrained local helper to apply a policy |
| **Root-owned local helper** | Enforce an allowlisted host-specific policy and implement local `status` / `recover` actions |
| `MissionControl.Archive` (optional) | Append-only operational evidence events; **not** a command-delivery channel or source of truth for execution |

**No arbitrary shell argument is ever forwarded from Dashboard or Mobile to the privileged helper.**

## 3. Containment levels

| Mode | Effect | Recovery path | MVP? |
| --- | --- | --- | --- |
| **Public Lockdown** | Deny new public application ingress, while preserving specifically approved management connectivity | Remote management or local console | **Yes** |
| **Quarantine** | Restrict inbound **and outbound** application traffic to an explicitly defined emergency allowlist | Management WireGuard/SSH if verified; otherwise provider console | Later |
| **Full Isolation** | Deliberately sever nearly all host network connectivity, potentially including management and Agent communication | **Provider's independently verified out-of-band console/VNC** and local recovery script | Last, with extra safeguards |

Containment should be **per-host**, not one global firewall profile. Clanker and ScopeCreep also participate in DN42 routing, WireGuard peering, DNS, and other network paths; FrontDesk and Wurstcase have backup/monitoring responsibilities. A single careless rule could break transit or strand dependent hosts.

The UI must preview which services, network interfaces, paths, and remote capabilities a policy is expected to affect. In particular, the operator must understand whether an action will interrupt SSH, existing WireGuard overlays, BGP, backups, DNS, or the MissionControl control path.

### Mode behavior guidelines

**Public Lockdown** should be the default recommendation for an urgent public-facing web-service vulnerability. Preserve a known-good management interface and recovery mechanism. Do not assume management VPN access remains trustworthy in every compromise scenario.

**Quarantine** requires an explicit, host-specific egress allowlist. Consider DNS, package repositories, vulnerability-response access, NTP, outbound log/alert delivery, and management traffic: allow only what is necessary, and clearly report the resulting limitations.

**Full Isolation** is intentionally a console-recovery event. Do not present a central **Restore All** button as reliable when the network path to those hosts has been intentionally destroyed. The operator must be able to re-enable each server through its provider console without the controller or DNS.

## 4. High-level control flow

```text
Operator (phone browser / future MAUI)
    |
    | HTTPS + authenticated session + fresh step-up verification
    v
MissionControl.Dashboard (authorization, confirmation, audit)
    |
    | authenticated, narrowly scoped control channel
    | (separate from existing public GET /api/snapshot)
    v
MissionControl.Agent on each selected VPS
    |
    | fixed action identifier, no shell arguments
    v
Local root-owned containment helper / dedicated systemd unit
    |
    +--> preflight -> apply known policy -> verify local state
    |
    +--> persist status for next boot / local console recovery

Command state and acknowledgements -> MissionControl Dashboard
```

The existing Dashboard → Agent *read* topology can be reused conceptually, but **command transport must be explicitly designed and authenticated**. Two plausible models:

1. **Agent polls a secured command queue** exposed by Dashboard/control service. This avoids exposing a new privileged Agent API to inbound network traffic, but requires a reachable controller when a command is issued.
2. **Dashboard calls a separate, private, authenticated Agent control endpoint** protected by mutual authentication/identity plus strict network rules. This may be simpler with the existing fleet topology but adds an inbound control surface.

**Decision gate:** choose the transport after reviewing live topology and failure modes. Do not reuse the existing unauthenticated snapshot endpoint, and do not treat NATS or the public Gateway event API as an implicit authorization channel. The historical event stream is for **evidence**, not authoritative command execution.

If MissionControl Dashboard is hosted on a VPS that is itself a target, its control path can disappear during isolation. The system must explicitly account for that: either host the emergency controller outside the target failure domain or make containment **pre-arranged and locally executable**, with clear partial-status semantics. Applying a fleet lockdown is not an atomic distributed transaction.

## 5. Privilege and authentication boundaries

This feature is a fleet-wide denial-of-service capability if misused. Treat it as highly privileged infrastructure administration, **not** an ordinary dashboard action.

### Operator authentication

- Require a separately granted **emergency administrator** permission. Default deny.
- Require **fresh step-up authentication** immediately before destructive changes (ideally WebAuthn/passkeys with user verification). A normal Dashboard cookie or stored Mobile bearer token alone must **not** authorize emergency actions.
- Require a second explicit confirmation showing **exact target hosts, policy, expected loss of access, and recovery method**. Consider typing a confirmation phrase for full isolation.
- Protect against CSRF, replay, duplicate submissions and accidental taps. Rate-limit attempts; short expiration is important.
- Prefer an authenticated mobile browser experience first; add MAUI-specific biometric/passkey support only after a secure server-side flow exists.
- Fail closed if required authorization or host policy validation is unavailable.

### Command security

- Use **versioned, strongly typed** commands: for example `ApplyPolicy(hostId, policyId, commandId, issuedAt, expiresAt)`.
- Bind commands to a specific authenticated operator, target host identity, approved policy ID, and short validity window.
- Authenticate the actual control channel and use per-Agent identities/credentials rather than one fleet-wide shared secret.
- Agents must reject expired, duplicate, wrong-host, unapproved, malformed, or downgraded commands.
- Record command ID and state persistently on the host so retries are **idempotent** and reboot does not cause an unsafe replay.
- Never accept firewall command lines, executable paths, arbitrary JSON configuration, or shell snippets from an API request.
- Do not expose reusable infrastructure credentials in frontend responses, browser storage or the Mobile app.

### Local privilege separation

- Keep the normal Agent unprivileged where practical; **do not simply run the Agent as root**.
- Use an explicit root-owned helper or a tightly constrained systemd service whose allowed action names are fixed. Validate all caller inputs even when invoked locally.
- Make policy definitions root-owned and unwritable by the Agent or Dashboard process.
- Ensure the helper cannot be used to execute arbitrary shell commands or select arbitrary network interfaces/rules.
- Note that Agents with Docker-socket access may **already be root-equivalent**. A helper does not create a true security boundary until those existing privileges are audited and reduced where possible.
- Treat a compromised Dashboard or agent process as part of the threat model and assess the blast radius before production rollout.

## 6. Local firewall containment implementation

The host helper should apply **prevalidated, reversible, host-specific policies**. It should own a dedicated, clearly named containment ruleset rather than editing arbitrary existing UFW/iptables/nftables rules on demand.

Design constraints:

- Detect supported firewall stack(s) before arming; establish precedence and interaction with existing UFW/nftables, routing rules, DN42/BIRD, Docker NAT and WireGuard.
- Install and validate policy ahead of time. An emergency action selects an already-reviewed policy; it doesn't assemble commands on the fly.
- Never flush all firewall tables, delete default routes, disable interfaces broadly, or modify provider-facing networking during initial development.
- Snapshot **only the state necessary for a safe rollback**; avoid a stale whole-firewall restore that erases unrelated legitimate changes made during recovery.
- Explicitly decide what happens to **existing connections**, not just new connections. Test established SSH/WireGuard sessions, forwarded DN42 traffic and IPv4/IPv6 behavior.
- Ensure containment persists across host reboot if that is the intended policy, without creating an unbootable or permanently unreachable machine.
- Define exactly what the local `recover` command changes and confirm that a second `recover` is harmless.
- Avoid reliance on network downloads, remote package repositories, DNS or MissionControl availability for recovery.

**Preflight is mandatory before first real deployment:** a disposable VM, working provider-console equivalent, rollback capability, and separate observers checking what remains reachable. No production-wide firewall test before local recovery is proven.

### Time-limited test mode

For **non-full-isolation** modes, consider a temporary apply/test option with a locally scheduled rollback independent of Dashboard. A trial remains active only after explicit operator confirmation from the recovered management path. This protects against a misconfigured allowlist during early deployments.

For deliberate **full isolation**, the product must clearly state whether rollback is impossible until console recovery, or whether a pre-armed local expiration is configured. Do not make ambiguous promises of remote recovery.

## 7. Per-host operational state and honest reporting

Suggested command lifecycle:

```text
Created -> Authorized -> Dispatched -> Received -> Preflighted
        -> Applying -> Applied -> Verified
                    \-> Failed / Rejected / Expired
```

Use **per-host** status; the fleet action is the aggregate of host results. Distinguish:

- **Dispatched**: Dashboard attempted delivery.
- **Received**: Agent acknowledged the command.
- **Applied**: helper reported that policy application completed.
- **Verified**: an independent/local check validated the target policy's expected effect.
- **Unknown**: the host stopped responding; this does **not** prove lockdown succeeded.

Full isolation may sever the connection before a final acknowledgement. One design is to locally **arm a delayed application**, acknowledge successful arming, then apply after the response is sent; even then, actual enforcement may remain unconfirmed from Dashboard. Never show an unqualified green success for a disconnected host.

The UI should display host, mode, timestamp, command ID, last contact, status, and recovery guidance. Store only necessary audit metadata; keep credentials and exploit-sensitive details out of logs.

## 8. Recovery and break-glass runbook (must exist first)

Every supported host needs an independently tested, locally available recovery path, such as:

```text
/usr/local/sbin/zeroday-recover status
/usr/local/sbin/zeroday-recover restore
```

These are **proposed interface names**, not existing commands. Implementation must be safe, idempotent, and auditable. Recovery must not require MissionControl, a working SSH connection, credentials stored only on an isolated host, or a live network connection.

Required operator procedure:

1. Open the hosting provider's **out-of-band console** using credentials/MFA verified **before** an incident. Confirm the console works if the public network interface is disabled.
2. Inspect recorded containment state and local logs. Verify the host and intended scope.
3. Restore only the containment-owned rules/policy, leaving unrelated firewall, WireGuard and routing configuration intact.
4. Verify public routing, management tunnel, DNS, SSH and relevant services **in stages**, rather than immediately reopening every public service.
5. Patch/mitigate the affected software, verify configuration and logs, then remove remaining restrictions deliberately.
6. Confirm backup/monitoring health and record incident outcome. **Do not treat removing lockdown as proof that compromise is remediated.**

For confirmed compromise, consider preserving evidence, rotating secrets and rebuilding from a trusted image; merely removing containment and applying a patch may be insufficient.

## 9. Failure cases to design and test

| Scenario | Required behavior |
| --- | --- |
| Host unreachable before action | Show **not delivered** / unknown; do not imply success |
| Control plane crashes partway through a fleet operation | Each host has independent command state; recovery and audit remain possible |
| Agent or network dies while applying policy | Locally stored state and recovery still work; UI reports unknown as appropriate |
| Command retried, duplicated or delivered out of order | Idempotent command IDs and monotonic policy state prevent unintended reapplication |
| Controller is itself selected for isolation | Treat as a special risk; arrange delivery/arming order and expect lost control-plane visibility |
| Wrong host targeted | Host-bound command identity rejects the action |
| Expired or replayed command | Reject without touching the firewall |
| IPv6 bypasses an IPv4-only restriction | Test both families; fail preflight if policy isn't comprehensive |
| Existing SSH or WireGuard connection drops | Provider-console recovery remains usable; test before shipping |
| Provider console is unavailable | Full isolation is **not armed or offered** for that host |
| DNS, NTP, internet or MissionControl unavailable | Recovery tooling remains offline-capable |
| Rule interaction with UFW, Docker NAT, BIRD or other policy routing | Document and verify the exact effect; no unreviewed global reset |
| Attacker steals a normal Mobile API bearer token | It alone must **not** grant containment authority |
| Dashboard / Agent compromise | Contain privilege blast radius; consider whether emergency control must be independently hosted/authenticated |

## 10. Incremental implementation plan

### Phase 0 — Threat model and inventory

- [ ] Inspect MissionControl `dev` implementation and current deployment: hosting location, trusted network paths, auth flow, Agent privileges, Docker socket and firewall access.
- [ ] Enumerate each VPS's firewall manager, SSH recovery route, interfaces, WireGuard, BIRD/DN42 forwarding, IPv6, boot persistence, and provider console access.
- [ ] Decide whether Dashboard is sufficiently outside the target failure domain; document what happens if it is isolated.
- [ ] Create a host-specific containment matrix: what stays up, what goes down, and how to recover.

**Gate:** A documented, testable failure and recovery model before coding firewall changes.

### Phase 1 — Local-only containment prototype

- [ ] Build fixed-policy helper on a **disposable Linux VM**, with no remote execution API.
- [ ] Implement `status`, trial lockdown, `restore`, idempotence and crash/reboot persistence.
- [ ] Test IPv4/IPv6, ingress/egress, established connections and interaction with local firewall.
- [ ] Test recovery through a console with all network connections intentionally unavailable.

**Gate:** Repeated successful isolated recovery tests, no host routing or unrelated rules damaged.

### Phase 2 — Secure one-host MissionControl control path

- [ ] Add explicit command/status contracts, strictly scoped Agent command handling and durable local command state.
- [ ] Design transport and per-host authentication independently of the current unauthenticated snapshot API.
- [ ] Add Dashboard emergency permissions, fresh step-up, action preview, confirmation and audit.
- [ ] Add **one-host Public Lockdown** in a test environment only; preserve the known management path.
- [ ] Test duplicate, delayed, wrong-host, expired and lost-acknowledgement commands.

**Gate:** Safe, authenticated, reversible end-to-end execution on a disposable node.

### Phase 3 — Carefully controlled production rollout

- [ ] Prepare and independently test console recovery on **one** VPS first; no fleet-wide button yet.
- [ ] Install reviewed local policies and constrained helper with explicit rollback artifacts.
- [ ] Trial a public-ingress lockdown with locally scheduled rollback; verify monitoring, SSH, WireGuard, backups, DNS and service impact.
- [ ] Enable one-host on-demand lockdown; verify operator identity, successful status and recovery audit.
- [ ] Repeat host by host only after reviewing each host's distinct network responsibilities.

**Gate:** Every enrolled host has a tested recovery runbook and demonstrated remote/console restoration.

### Phase 4 — Fleet workflow and mobile experience

- [ ] Add multi-host selection with **individual per-host state** and partial-failure reporting.
- [ ] Add a phone-friendly Dashboard emergency page; reuse shared UI only where the authenticated flow stays correct.
- [ ] Consider MAUI-native flow later, including secure reauthentication rather than relying on the existing bearer token.
- [ ] Add independent alerting and auditable event summaries after command state transitions; do not treat NATS archive delivery as proof of policy application.
- [ ] Practice a multi-host mock incident where one node is unreachable and another loses connectivity during action.

**Gate:** User can identify exactly which hosts are confirmed contained, unknown, or failed.

### Phase 5 — Quarantine and full isolation (optional, highest risk)

- [ ] Implement and test narrow egress allowlists and host-specific dependencies.
- [ ] Add a distinctly labeled **Full Isolation / Provider Console Required** action, disabled unless out-of-band access has been checked.
- [ ] Explicitly define controller-last behavior, command arming and non-atomic execution semantics.
- [ ] Test offline recovery after reboot and failed/partial isolation.
- [ ] Require separate approval/step-up and prominent irreversibility warnings.

**Gate:** No host can be offered full isolation without validated console recovery and a pretested local restore path.

## 11. MVP definition of done

A useful first release can be **one-host Public Lockdown** without ever building a full network kill switch:

- [ ] Operator authenticates and completes a fresh privileged confirmation.
- [ ] Dashboard displays the exact target, policy and expected affected services before execution.
- [ ] Agent accepts only an authenticated, approved host-bound command.
- [ ] Constrained local helper activates a pre-reviewed policy without disrupting unrelated host routing/firewall state.
- [ ] Selected public services become unreachable from a separate test vantage point, while the approved management path remains usable.
- [ ] Local console `restore` works without Dashboard or internet connectivity.
- [ ] Repeated apply/recover and service reboot are safe; duplicate commands have no additional side effects.
- [ ] UI accurately distinguishes applied, verified, failed and unknown status; action is recorded in an audit log.
- [ ] Automated tests cover authentication, replay, idempotence, partial failure, timeout, validation and cancellation.
- [ ] Deployment and rollback instructions live alongside the code in InfraOps/MissionControl documentation.

## 12. Open design decisions

1. **Controller independence:** Where does MissionControl Dashboard actually run relative to the VPSes that could be isolated? Is an independent control-plane host needed?
2. **Transport:** Outbound polling or a separate authenticated private Agent control API? What is reachable from the phone if management tunnels are impaired?
3. **Privilege boundary:** What capabilities does the existing Agent service actually possess on each host (particularly Docker socket access)? Is a separate narrowly privileged local service necessary to preserve a meaningful boundary?
4. **Policy engine:** How should existing UFW/nftables, Docker NAT and DN42 forwarding be handled without disrupting unrelated rules? Do Debian and FreeBSD targets require distinct implementations?
5. **Scope:** Does the first version cover only public web ingress, or also DNS, BGP peers, Restic REST, overlay tunnels and service-specific vulnerabilities?
6. **Recovery:** Which provider consoles are independently reachable? Which hosts should **never** offer full isolation until a tested emergency console exists?
7. **Audit:** Which data is retained centrally, which is retained locally, and how are missing acknowledgements represented?
8. **Policy expiration:** Should trial lockdown have an automatic local rollback, and how is a deliberate permanent containment explicitly confirmed?

## 13. Recommended immediate next step

**Do not implement an all-VPS kill button first.** Build a local-only, reversible Public Lockdown prototype on a disposable VM, with the console recovery command and tests completed **before** wiring in MissionControl Agent or a phone UI.

Then add a single-host Dashboard workflow and validate the security boundary. Fleet-wide and full-isolation actions can follow only after recovery has been demonstrated on each host.

---

**Repository note:** This document is a proposal, not proof that a helper, privilege separation, authenticated control path, or console recovery mechanism exists. Keep it on `dev` initially and verify the current implementation/deployment details before turning any proposed command or policy into a live system change.
