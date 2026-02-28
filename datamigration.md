# Data Migration — Baremetal → Kubernetes

Baremetal deploy path: `/srv/DiscordBots/AGCUtilsV2`
Target cluster namespace: `agc`

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

```bash
# Pack on the server
ssh user@server \
  "tar -czf /tmp/transcripts.tar.gz \
     -C /srv/DiscordBots/AGCUtilsV2/data/tickets/transcripts ."

# Pull archive locally
scp user@server:/tmp/transcripts.tar.gz /tmp/transcripts.tar.gz

# Push into pod and extract
kubectl cp /tmp/transcripts.tar.gz agc/migrate-transcripts:/tmp/transcripts.tar.gz
kubectl exec -n agc migrate-transcripts -- \
  sh -c "tar -xzf /tmp/transcripts.tar.gz -C /data && echo 'done'"
```

### 1.4 Verify

```bash
# File count should match the server
kubectl exec -n agc migrate-transcripts -- sh -c "find /data -type f | wc -l"
ssh user@server "find /srv/DiscordBots/AGCUtilsV2/data/tickets/transcripts -type f | wc -l"
```

### 1.5 Tear down migration pod

```bash
kubectl delete pod -n agc migrate-transcripts
ssh user@server "rm /tmp/transcripts.tar.gz"
```

---

## 2. Images (flag\_images + warn\_images)

**Source:** `/srv/DiscordBots/AGCUtilsV2/flag_images/` and `…/warn_images/`
**Target PVC:** `agc-images` (5 Gi) → subdirs `flag_images/` and `warn_images/`

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
# Pack both dirs in one archive
ssh user@server \
  "tar -czf /tmp/images.tar.gz \
     -C /srv/DiscordBots/AGCUtilsV2 \
     flag_images warn_images"

scp user@server:/tmp/images.tar.gz /tmp/images.tar.gz

kubectl cp /tmp/images.tar.gz agc/migrate-images:/tmp/images.tar.gz
kubectl exec -n agc migrate-images -- \
  sh -c "tar -xzf /tmp/images.tar.gz -C /images && echo 'done'"
```

### 2.3 Verify

```bash
kubectl exec -n agc migrate-images -- sh -c "
  echo 'flag_images:' && ls /images/flag_images | wc -l
  echo 'warn_images:' && ls /images/warn_images | wc -l
"
```

### 2.4 Tear down migration pod

```bash
kubectl delete pod -n agc migrate-images
ssh user@server "rm /tmp/images.tar.gz"
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
