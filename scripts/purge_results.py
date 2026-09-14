import os
import sys
from datetime import datetime, timedelta, timezone

from azure.data.tables import TableServiceClient
from azure.identity import DefaultAzureCredential


def main() -> int:
    account_url = os.getenv("STORAGE_ACCOUNT_URL", "").rstrip("/")
    table_name = os.getenv("RESULTS_TABLE", "detections")
    days = int(sys.argv[1] if len(sys.argv) > 1 else os.getenv("RESULTS_RETENTION_DAYS", "30"))
    if not account_url:
        print("set STORAGE_ACCOUNT_URL")
        return 1

    cutoff = (datetime.now(timezone.utc) - timedelta(days=days)).strftime("%Y-%m-%dT%H:%M:%SZ")
    client = TableServiceClient(
        endpoint=account_url.replace(".blob.", ".table."),
        credential=DefaultAzureCredential(),
    ).get_table_client(table_name)

    deleted = 0
    for entity in client.list_entities():
        if entity.get("processed_at_utc", "9999") < cutoff:
            client.delete_entity(partition_key=entity["PartitionKey"], row_key=entity["RowKey"])
            deleted += 1
    print(f"deleted {deleted} records older than {days} days (cutoff {cutoff})")
    return 0


if __name__ == "__main__":
    sys.exit(main())
