import os
import requests
import json

token = os.environ.get("GH_TOKEN")
headers = {
    "Authorization": f"token {token}",
    "Accept": "application/vnd.github.v3+json"
}
url = "https://api.github.com/repos/holstebroe/MeshWave/code-scanning/alerts"

response = requests.get(url, headers=headers)
alerts = response.json()

for alert in alerts:
    print(f"Alert #{alert['number']}: {alert['rule']['id']}")
    print(f"  Severity: {alert['rule']['security_severity_level']}")
    print(f"  State: {alert['state']}")
    print(f"  Path: {alert['most_recent_instance']['location']['path']}")
    print(f"  Message: {alert['most_recent_instance']['message']['text']}")
    print("-" * 20)
