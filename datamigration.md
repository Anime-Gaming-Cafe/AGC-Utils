# Data Migration — Baremetal → Kubernetes

Baremetal deploy path: `/srv/DiscordBots/AGCUtilsV2`
Target cluster namespace: `agc`

---

## Prerequisites — Running on Windows

The streaming commands (`ssh … | kubectl exec -i …`) require a Unix-like shell that passes **binary data** through pipes unmodified. PowerShell corrupts binary streams; use one of these instead:

| Shell | Works? | Notes |
|---|---|---|
| **Git Bash / MSYS2** | ✅ | Prefix `kubectl exec` with `MSYS_NO_PATHCONV=1` (see below) |
| **WSL 2** | ✅ | `kubectl` must be in PATH inside WSL (or symlink from Windows: `ln -s /mnt/c/…/kubectl.exe /usr/local/bin/kubectl`) |
| **PowerShell 7** | ⚠️ | Binary pipes are unreliable — use the fallback below |
| **PowerShell 5 / cmd** | ❌ | Do not use for streaming binary data |

**Recommended:** open Git Bash and run all commands there. `kubectl`, `ssh`, and `kubectl exec` all work the same as on Linux.

> **Git Bash path conversion gotcha:** Git Bash silently rewrites Unix absolute paths (e.g. `/data`) to Windows paths (`C:/Program Files/Git/data`) before passing them to child processes. Any `kubectl exec -- … /some/path` will break without this prefix:
> ```bash
> MSYS_NO_PATHCONV=1 kubectl exec …
> ```
> All `kubectl exec` commands in this guide already include it.

### PowerShell fallback (when you can't use Git Bash / WSL)

If the server has enough disk space for an intermediate archive, copy it in two steps:

```powershell
# 1. Pack on the server
ssh user@server "tar -czf /tmp/transcripts.tar.gz -C /srv/DiscordBots/AGCUtilsV2/data/tickets/transcripts ."

# 2. Pull to local disk (needs ~= compressed size of transcripts locally)
scp user@server:/tmp/transcripts.tar.gz $env:TEMP\transcripts.tar.gz

# 3. Upload into pod and extract
kubectl cp "$env:TEMP\transcripts.tar.gz" agc/migrate-transcripts:/tmp/transcripts.tar.gz
kubectl exec -n agc migrate-transcripts -- sh -c "tar -xzf /tmp/transcripts.tar.gz -C /data && rm /tmp/transcripts.tar.gz"

# 4. Cleanup
Remove-Item "$env:TEMP\transcripts.tar.gz"
ssh user@server "rm /tmp/transcripts.tar.gz"
```

---

## 1. Ticket Transcripts

**Source:** `/srv/DiscordBots/AGCUtilsV2/data/tickets/transcripts/`
**Target PVC:** `agc-transcripts` (30 Gi) → mounted at `/app/data/tickets/transcripts`

### 1.1 Stop the baremetal bot

```bash
ssh user@server
sudo systemctl stop agc-utilsv2
```

### 1.2 Spin up a migration pod

```bash
kubectl apply -n agc -f - <<'EOF'
apiVersion: v1
kind: Pod
metadata:
  name: migrate-transcripts
  namespace: agc
spec:
  restartPolicy: Never
  containers:
    - name: sh
      image: alpine:3.21
      command: ["sleep", "infinity"]
      volumeMounts:
        - name: transcripts
          mountPath: /data
  volumes:
    - name: transcripts
      persistentVolumeClaim:
        claimName: agc-transcripts
EOF

kubectl wait pod -n agc migrate-transcripts --for=condition=Ready --timeout=60s
```

### 1.3 Copy files into the PVC

Transfer directly inside the pod — data flows **server → pod** without touching your local machine.

```bash
# 1. Install SSH client in the pod
MSYS_NO_PATHCONV=1 kubectl exec -n agc migrate-transcripts -- apk add --no-cache openssh-client

# 2. Copy your SSH private key into the pod (tiny file, fine to relay through PC)
kubectl cp ~/.ssh/id_ed25519 agc/migrate-transcripts:/tmp/id_ed25519

# 3. Pull data directly from server into the PVC
MSYS_NO_PATHCONV=1 kubectl exec -n agc migrate-transcripts -- sh -c "
  chmod 600 /tmp/id_ed25519 &&
  ssh -i /tmp/id_ed25519 -o StrictHostKeyChecking=no root@main.diamondforge.me \
    'tar -czf - -C /srv/DiscordBots/AGCUtilsV2/data/tickets/transcripts .' \
    | tar -xzf - -C /data &&
  rm -f /tmp/id_ed25519 &&
  echo 'Transfer done'
"
```

Data path:
```
Server ──SSH──► Pod ──► /data (PVC)
                  ↑
          no local PC involved
```

### 1.4 Verify

```bash
# File count should match the server
MSYS_NO_PATHCONV=1 kubectl exec -n agc migrate-transcripts -- sh -c "find /data -type f | wc -l"
ssh user@server "find /srv/DiscordBots/AGCUtilsV2/data/tickets/transcripts -type f | wc -l"
```

### 1.5 Tear down migration pod

```bash
kubectl delete pod -n agc migrate-transcripts
```

---

## 2. Images (flag + warn)

**Source:** `/srv/DiscordBots/AGCUtilsV2/data/images/flag/` and `…/warn/`
**Target PVC:** `agc-images` (5 Gi) → subdirs `flag/` and `warn/`

> The init container in the Deployment creates these subdirs automatically on first pod start.
> Run the migration pod below **before** starting the Deployment so the PVC is pre-populated.

### 2.1 Spin up a migration pod

```bash
kubectl apply -n agc -f - <<'EOF'
apiVersion: v1
kind: Pod
metadata:
  name: migrate-images
  namespace: agc
spec:
  restartPolicy: Never
  containers:
    - name: sh
      image: alpine:3.21
      command: ["sleep", "infinity"]
      volumeMounts:
        - name: images
          mountPath: /images
  volumes:
    - name: images
      persistentVolumeClaim:
        claimName: agc-images
EOF

kubectl wait pod -n agc migrate-images --for=condition=Ready --timeout=60s
```

### 2.2 Copy files into the PVC

```bash
# Install SSH client in the pod
MSYS_NO_PATHCONV=1 kubectl exec -n agc migrate-images -- apk add --no-cache openssh-client

# Copy SSH key
kubectl cp ~/.ssh/id_ed25519 agc/migrate-images:/tmp/id_ed25519

# Pull directly from server
MSYS_NO_PATHCONV=1 kubectl exec -n agc migrate-images -- sh -c "
  chmod 600 /tmp/id_ed25519 &&
  ssh -i /tmp/id_ed25519 -o StrictHostKeyChecking=no root@main.diamondforge.me \
    'tar -czf - -C /srv/DiscordBots/AGCUtilsV2/data/images flag warn' \
    | tar -xzf - -C /images &&
  rm -f /tmp/id_ed25519 &&
  echo 'Transfer done'
"
```

### 2.3 Verify

```bash
MSYS_NO_PATHCONV=1 kubectl exec -n agc migrate-images -- sh -c "
  echo 'flag:' && ls /images/flag | wc -l
  echo 'warn:' && ls /images/warn | wc -l
"
```

### 2.4 Tear down migration pod

```bash
kubectl delete pod -n agc migrate-images
```

---

## 3. Cut-over

Once both migrations are verified:

```bash
# 1. Let ArgoCD sync the Deployment (or trigger manually)
kubectl apply -f k8s/argocd-app.yaml

# 2. Watch the rollout
kubectl rollout status deployment/agc-utils -n agc

# 3. Smoke-test a transcript and an image
curl -I https://ticketsystem.animegamingcafe.de/transcripts/<any>.html
curl -I https://i.agc-intern.com/flag_images/<any>.png

# 4. Decommission the baremetal service
ssh user@server "sudo systemctl disable --now agc-utilsv2"
```
