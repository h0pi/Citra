# Deploying Citra on an Azure VM (Azure for Students)

The whole app runs on one small Linux VM with Docker Compose:

```
visitor ──HTTPS──▶ Caddy (80/443, automatic Let's Encrypt certificate)
                     └─▶ web: nginx (admin app; forwards /api, /hubs, /health, /widget.js)
                           └─▶ api ──▶ db (PostgreSQL + pgvector)
                                worker ──┘   (shared volume for uploaded files)
                     backup: nightly pg_dump into ./backups (last 7 days)
```

**Cost.** Azure for Students needs no card. It gives a $100 credit for 12 months, and you can renew it while you study.

- The VM size **B2ats v2** is in Azure's free services: 750 hours a month, which is one VM all month.
- The disk is a 64 GB Premium SSD (P6), which is free for the first 12 months.
- The public IP address costs about $3–4 a month, taken from the credit.

## 1. Create the VM (once)

1. In the Azure portal, open **Virtual machines → Create → Azure virtual machine**.
2. On the **Basics** tab:
   - **Subscription:** Azure for Students.
   - **Resource group:** create a new one called `citra`.
   - **Virtual machine name:** `citra`.
   - **Region:** one close to you, such as *Germany West Central*, *Italy North*, *Poland Central* or *North Europe*. Student subscriptions only allow some regions. If Azure refuses one, pick another.
   - **Availability options:** No infrastructure redundancy required.
   - **Security type:** Standard.
   - **Image:** *Ubuntu Server 24.04 LTS – x64 Gen2*.
   - **Size:** *Standard_B2ats_v2* (2 vCPU, 1 GiB), marked "free services eligible". Use *See all sizes* if it isn't listed.
   - **Authentication:** SSH public key. **Username:** `azureuser`. **Key:** *Generate new key pair*, named `citra_key`.
   - **Inbound ports:** allow **SSH (22), HTTP (80), HTTPS (443)**.
3. On the **Disks** tab, set the **OS disk size** to **64 GiB (P6)** and the type to **Premium SSD**. Both are free for 12 months.
4. On the **Networking** tab, keep the defaults and tick **Delete public IP and NIC when VM is deleted**.
5. On the **Management** tab, check that **Auto-shutdown** is **off**.
6. Click **Review + create → Create**, then **Download private key and create resource**. Keep `citra_key.pem` safe.

## 2. A free address

1. Open the VM's **Overview** page and click **Not configured** next to **DNS name**.
2. Enter a **DNS name label**, for example `citra-demo`, and click **Save**.
3. Your address is now `citra-demo.<region>.cloudapp.azure.com`, for example `citra-demo.germanywestcentral.cloudapp.azure.com`.

## 3. Prepare the VM (once)

From Windows, first make the key private to you; OpenSSH refuses keys others can read:

```powershell
icacls citra_key.pem /inheritance:r /grant:r "$($env:USERNAME):R"
ssh -i citra_key.pem azureuser@<your-address>
```

Then run on the VM:

```bash
# 1 GB of memory is enough to run Citra, but not to build it: add 4 GB of swap (kept after restarts).
sudo fallocate -l 4G /swapfile && sudo chmod 600 /swapfile && sudo mkswap /swapfile && sudo swapon /swapfile
echo '/swapfile none swap sw 0 0' | sudo tee -a /etc/fstab

# Docker (official install script), usable without sudo after logging in again.
curl -fsSL https://get.docker.com | sudo sh
sudo usermod -aG docker azureuser
exit
```

## 4. Install Citra

```bash
ssh -i citra_key.pem azureuser@<your-address>
git clone https://github.com/h0pi/Citra.git    # private repo: use a read-only access token as the password
cd Citra
cp .env.example .env
nano .env    # fill in POSTGRES_PASSWORD, JWT_SIGNING_KEY, VOYAGE_API_KEY, CITRA_DOMAIN=<your-address>
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

The first build takes a while on the small VM (about 20–40 minutes with swap); later builds reuse the cache. Then:

- `https://<your-address>/health` should say `Healthy`.
- `https://<your-address>` opens the login page.
- `docker compose logs -f api worker` shows what the app is doing.

Generate random secrets on the VM with `openssl rand -base64 48`.

## 5. The demo company

Run this from your own computer, against the live site:

```bash
python demo-data/seed.py --api https://<your-address> --email owner@northwind-outdoor.example --password '<a strong password>'
```

Then set `DEMO_TENANT_SLUG=northwind-outdoor` in `.env` on the VM, and restart:

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d
```

## Updating

```bash
cd Citra && git pull
docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d --build
```

The API applies database migrations itself when it starts.

## Backups

A dump is written every day to `~/Citra/backups/citra-YYYY-MM-DD.dump`, and the last 7 days are kept. To keep a copy off the VM, run this from your computer:

```bash
scp -i citra_key.pem azureuser@<your-address>:Citra/backups/citra-*.dump .
```

To restore, replacing everything in the database:

```bash
docker compose -f docker-compose.yml -f docker-compose.prod.yml stop api worker
docker compose -f docker-compose.yml -f docker-compose.prod.yml exec -T backup \
  pg_restore --clean --if-exists --dbname=citra /backups/citra-YYYY-MM-DD.dump
docker compose -f docker-compose.yml -f docker-compose.prod.yml start api worker
```

Uploaded files are in the Docker volume `citra_storage`, and the database is in `citra_db-data`. Both live on the VM's disk and survive restarts and updates. Only `docker compose down -v` deletes them.

## Keep the credit safe

In the Azure portal, open **Cost Management → Budgets → Add** and set a budget of **$10 a month** with an email alert at 80%. That way you hear about any unexpected cost long before the credit runs out.
