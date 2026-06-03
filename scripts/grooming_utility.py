import os
import json
import subprocess
import argparse
import time

class GitHubClient:
    def __init__(self, repo):
        self.token = os.environ.get("GH_TOKEN")
        self.repo = repo
        if not self.token:
            raise ValueError("GH_TOKEN environment variable not set")

    def _call(self, path, method="GET", data=None):
        url = f"https://api.github.com/repos/{self.repo}{path}"
        cmd = [
            "curl", "-s", "-X", method,
            "-H", f"Authorization: token {self.token}",
            "-H", "Accept: application/vnd.github.v3+json",
            url
        ]
        if data:
            # Insert data argument before the URL
            cmd.insert(-1, "-d")
            cmd.insert(-1, json.dumps(data))

        result = subprocess.run(cmd, capture_output=True, text=True)
        if result.returncode != 0:
            print(f"Error calling GitHub API: {result.stderr}")
            return None
        try:
            return json.loads(result.stdout)
        except json.JSONDecodeError:
            print(f"Failed to decode response: {result.stdout}")
            return None

    def get_milestones(self, state="open"):
        return self._call(f"/milestones?state={state}")

    def get_issues(self, state="all"):
        issues = []
        page = 1
        while True:
            batch = self._call(f"/issues?state={state}&per_page=100&page={page}")
            if not batch:
                break
            issues.extend(batch)
            page += 1
        return issues

    def create_issue(self, title, body, milestone_number):
        data = {
            "title": title,
            "body": body,
            "milestone": milestone_number
        }
        return self._call("/issues", method="POST", data=data)

def main():
    parser = argparse.ArgumentParser(description="MeshWave GitHub Grooming Utility")
    parser.add_argument("--repo", default="holstebroe/MeshWave", help="GitHub repository (owner/name)")
    subparsers = parser.add_subparsers(dest="command")

    # Fetch command
    fetch_parser = subparsers.add_parser("fetch", help="Fetch milestones and issues to a JSON file")
    fetch_parser.add_argument("--output", default="github_state.json", help="Output JSON file path")

    # Analyze command
    analyze_parser = subparsers.add_parser("analyze", help="Analyze milestones and their linked issues")
    analyze_parser.add_argument("--input", default="github_state.json", help="Input JSON file path")

    args = parser.parse_args()
    client = GitHubClient(args.repo)

    if args.command == "fetch":
        print(f"Fetching data from {args.repo}...")
        milestones = client.get_milestones()
        issues = client.get_issues()
        data = {
            "milestones": milestones,
            "issues": issues
        }
        with open(args.output, "w") as f:
            json.dump(data, f, indent=2)
        print(f"Data saved to {args.output}")

    elif args.command == "analyze":
        if not os.path.exists(args.input):
            print(f"Input file {args.input} not found.")
            return

        with open(args.input, "r") as f:
            data = json.load(f)

        milestones = data["milestones"]
        issues = data["issues"]

        ms_to_issues = {ms["number"]: [] for ms in milestones}
        for issue in issues:
            if issue.get("milestone"):
                ms_num = issue["milestone"]["number"]
                if ms_num in ms_to_issues:
                    ms_to_issues[ms_num].append(issue)

        print(f"{'#':<3} | {'Title':<40} | {'Open':<5} | {'Closed':<6} | {'Issues Linked'}")
        print("-" * 80)
        for ms in milestones:
            num = ms["number"]
            title = ms["title"]
            open_i = ms["open_issues"]
            closed_i = ms["closed_issues"]
            linked = len(ms_to_issues[num])
            print(f"{num:<3} | {title[:40]:<40} | {open_i:<5} | {closed_i:<6} | {linked}")

    else:
        parser.print_help()

if __name__ == "__main__":
    main()
