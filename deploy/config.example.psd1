# Server settings. Copy to config.psd1 (next to this file, or into the release's server\ folder) and edit.
# config.psd1 holds no passwords: those are generated on the server and kept under <DataRoot>\secrets.
@{
    # ---- names ----
    ErpHost      = 'erp.example.com'                       # the ERP and the driver app
    WebsiteHosts = @('www.example.com', 'example.com')     # the public website; the FIRST one is the canonical address

    # ---- folders ----
    SitesRoot = 'C:\inetpub\shannana'    # program files (replaced on every upgrade)
    DataRoot  = 'D:\ShannanaData'        # what must survive upgrades: proof photos, product images, logs, backups, secrets

    # ---- ERP (SQL Server) ----
    SqlServer         = 'localhost'      # 'localhost' or 'localhost\SQLEXPRESS' for a database on this server
    ErpDbName         = 'ShannanaErp'
    LocalCurrencyCode = 'SYP'            # the ERP's local currency (first install only)

    # ---- website (PostgreSQL) ----
    PgHost   = '127.0.0.1'
    PgPort   = 5432
    PgDbName = 'shannana'
    PgUser   = 'shannana'
    NodePort = 3000                      # only listens on this machine; IIS forwards to it
    WhatsappNumber      = '963985034031' # digits only, international format
    OrderWhatsappNumber = '963985034031'

    # ---- HTTPS ----
    # Thumbprint of a certificate already installed in LocalMachine\My (for example one issued by win-acme). Leave empty to add it later.
    CertThumbprint = ''

    # ---- backups ----
    BackupKeepDays    = 14
    OffsiteBackupPath = ''               # optional: a share (\\nas\backups\shannana) that each night's backup is copied to
}
