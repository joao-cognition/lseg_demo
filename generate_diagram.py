"""
Generate the MarketDataHub Azure Target Architecture diagram.

Requirements:
    pip install diagrams graphviz
    sudo apt-get install graphviz   # system-level renderer

Usage:
    python generate_diagram.py

Produces:
    marketdatahub_architecture.pdf
"""

from diagrams import Cluster, Diagram, Edge
from diagrams.azure.compute import ContainerRegistries, FunctionApps, KubernetesServices
from diagrams.azure.database import SQLDatabases
from diagrams.azure.devops import ApplicationInsights
from diagrams.azure.identity import ActiveDirectory
from diagrams.azure.integration import EventGridDomains
from diagrams.azure.monitor import Monitor
from diagrams.azure.network import ApplicationGateway, ExpressrouteCircuits
from diagrams.azure.security import KeyVaults
from diagrams.azure.storage import BlobStorage
from diagrams.generic.compute import Rack
from diagrams.onprem.ci import GithubActions
from diagrams.onprem.client import Users
from diagrams.onprem.monitoring import Prometheus
from diagrams.onprem.network import Nginx

GRAPH_ATTR = {
    "fontsize": "28",
    "fontname": "Helvetica",
    "bgcolor": "white",
    "pad": "1.0",
    "nodesep": "0.8",
    "ranksep": "1.2",
    "splines": "ortho",
}

NODE_ATTR = {
    "fontsize": "11",
    "fontname": "Helvetica",
}

EDGE_ATTR = {
    "fontsize": "10",
    "fontname": "Helvetica",
}

CLUSTER_BLUE = {
    "style": "dashed",
    "color": "#4A90D9",
    "fontcolor": "#4A90D9",
    "fontsize": "14",
    "fontname": "Helvetica Bold",
    "penwidth": "2.0",
}

CLUSTER_GREEN = {**CLUSTER_BLUE, "color": "#2ECC71", "fontcolor": "#2ECC71"}
CLUSTER_ORANGE = {**CLUSTER_BLUE, "color": "#E67E22", "fontcolor": "#E67E22"}
CLUSTER_PURPLE = {**CLUSTER_BLUE, "color": "#8E44AD", "fontcolor": "#8E44AD"}
CLUSTER_RED = {**CLUSTER_BLUE, "color": "#E74C3C", "fontcolor": "#E74C3C"}
CLUSTER_TEAL = {**CLUSTER_BLUE, "color": "#1ABC9C", "fontcolor": "#1ABC9C"}
CLUSTER_GREY = {**CLUSTER_BLUE, "color": "#7F8C8D", "fontcolor": "#7F8C8D"}

with Diagram(
    "MarketDataHub \u2014 Azure Target Architecture",
    show=False,
    filename="marketdatahub_architecture",
    outformat="pdf",
    direction="LR",
    graph_attr=GRAPH_ATTR,
    node_attr=NODE_ATTR,
    edge_attr=EDGE_ATTR,
):
    # ── Client Systems ──────────────────────────────────────────────────
    with Cluster("Client Systems", graph_attr=CLUSTER_GREY):
        trading = Users("Trading Desks\n& Risk Engines")
        api_consumers = Users("API Consumers\n(REST / TCP)")

    # ── CI/CD Pipeline ──────────────────────────────────────────────────
    with Cluster("CI/CD Pipeline", graph_attr=CLUSTER_TEAL):
        gha = GithubActions("GitHub\nActions")
        acr = ContainerRegistries("Azure Container\nRegistry (ACR)")
        gha >> Edge(label="Docker push", color="#1ABC9C") >> acr

    # ── Application Gateway ─────────────────────────────────────────────
    appgw = ApplicationGateway("Azure Application\nGateway")

    trading >> Edge(color="#7F8C8D") >> appgw
    api_consumers >> Edge(color="#7F8C8D") >> appgw

    # ── On-Premises (ExpressRoute / VNet) ───────────────────────────────
    with Cluster("On-Premises (ExpressRoute / VNet)", graph_attr=CLUSTER_RED):
        er = ExpressrouteCircuits("ExpressRoute")
        fix = Rack("FIX Gateway\nfix-gw01:9876\n(FIX 4.2)")
        bbg = Rack("Bloomberg API\nbbg-api:8194")
        ref = Rack("Refinitiv Feed\nrefeed01:14002")

    # ── Azure Kubernetes Service (AKS) ──────────────────────────────────
    with Cluster("Azure Kubernetes Service (AKS)", graph_attr=CLUSTER_BLUE):
        with Cluster("Front End namespace", graph_attr={**CLUSTER_BLUE, "bgcolor": "#EBF5FB"}):
            ingress = Nginx("NGINX\nIngress")

        with Cluster("Back-End Services namespace", graph_attr={**CLUSTER_BLUE, "bgcolor": "#EBF5FB"}):
            mvc = KubernetesServices("MarketDataHub\nASP.NET MVC 5\n(Web + API)")
            pfs = KubernetesServices("PriceFeedService\nPod")
            idx = KubernetesServices("IndexCalcService\nPod")
            hpa = KubernetesServices("Pod Autoscaling\n(HPA)")

        with Cluster("Batch Jobs namespace", graph_attr={**CLUSTER_ORANGE, "bgcolor": "#FEF9E7"}):
            fn_eod = FunctionApps("Azure Function\nEOD Export\n(Timer 16:45 UTC)")
            fn_mifid = FunctionApps("Azure Function\nMiFID Report\n(Timer 18:00 UTC)")
            fn_archive = FunctionApps("Azure Function\nTick Archival\n(Timer 17:00 UTC)")

        with Cluster("Utility Services namespace", graph_attr={**CLUSTER_PURPLE, "bgcolor": "#F5EEF8"}):
            appins = ApplicationInsights("Application\nInsights Agent")
            prom = Prometheus("Prometheus")

    # ── Ingress flow ────────────────────────────────────────────────────
    acr >> Edge(label="Docker pull", color="#1ABC9C") >> mvc
    appgw >> Edge(color="#4A90D9") >> ingress >> Edge(color="#4A90D9") >> mvc
    mvc >> Edge(color="#4A90D9") >> pfs
    mvc >> Edge(color="#4A90D9") >> idx

    # ── On-prem → AKS feeds ────────────────────────────────────────────
    fix >> Edge(label="FIX 4.2\nvia VNet", color="#E74C3C") >> pfs
    bbg >> Edge(label="VNet\nIntegration", color="#E74C3C") >> pfs
    ref >> Edge(label="VNet\nIntegration", color="#E74C3C") >> pfs

    # ── External Data Stores ────────────────────────────────────────────
    with Cluster("External Data Stores", graph_attr=CLUSTER_GREEN):
        sql_pri = SQLDatabases("Azure SQL\nmdh-prod\n(Primary)")
        sql_tik = SQLDatabases("Azure SQL\nmdh-ticks\n(Tick Store)")
        sql_rep = SQLDatabases("Azure SQL\nmdh-reporting\n(Read Replica)")
        blob = BlobStorage("Azure Blob\nmdhstorage")

    mvc >> Edge(color="#2ECC71") >> sql_pri
    pfs >> Edge(color="#2ECC71") >> sql_tik
    mvc >> Edge(color="#2ECC71") >> sql_rep
    idx >> Edge(color="#2ECC71") >> sql_pri
    fn_eod >> Edge(color="#2ECC71") >> blob
    fn_mifid >> Edge(color="#2ECC71") >> blob
    fn_archive >> Edge(color="#2ECC71") >> blob

    # ── Identity & Security ─────────────────────────────────────────────
    with Cluster("Identity & Security", graph_attr=CLUSTER_PURPLE):
        aad = ActiveDirectory("Microsoft\nEntra ID\n(Azure AD)")
        kv = KeyVaults("Azure Key Vault\nmdh-vault")
        mon = Monitor("Azure Monitor")

    aad >> Edge(label="RBAC\n(Dev/Ops access)", color="#8E44AD") >> mvc
    kv >> Edge(label="Secrets &\nConn Strings", color="#8E44AD") >> mvc
    appins >> Edge(color="#8E44AD") >> mon

    # ── Notifications ───────────────────────────────────────────────────
    with Cluster("Notifications", graph_attr=CLUSTER_ORANGE):
        acs = Rack("Azure Communication\nServices (Email)")
        eg = EventGridDomains("Azure\nEvent Grid")

    mvc >> Edge(label="Email", color="#E67E22") >> acs
    fn_mifid >> Edge(label="FCA report\nready", color="#E67E22") >> eg
