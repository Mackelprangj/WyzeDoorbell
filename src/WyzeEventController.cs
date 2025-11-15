import os
import time
import json
import requests
from datetime import datetime, timedelta, timezone

# Install required libraries: pip install wyze-sdk requests
from wyze_sdk import Client
from wyze_sdk.api.client import Client as WyzeClient

# --- CONFIGURATION ---
# Target C# API Endpoint (Change this to your actual C# server URL)
TARGET_API_URL = "http://localhost:5000/api/wyze/doorbell"

# Wyze Device ID of your doorbell (VDB = Video Doorbell)
# NOTE: Replace with your actual device MAC or use environment variable
DOORBELL_MAC = os.environ.get("DOORBELL_MAC_ID", "YOUR_DOORBELL_MAC_HERE")
POLLING_INTERVAL_SECONDS = 5
EVENT_TYPE_BUTTON_PRESS = 2005 # Common code for Doorbell Button Press

# Authentication details (Using environment variables is recommended)
WYZE_EMAIL = os.environ.get("WYZE_EMAIL")
WYZE_PASSWORD = os.environ.get("WYZE_PASSWORD")

# Initialize Wyze Client
try:
    if not WYZE_EMAIL or not WYZE_PASSWORD:
        raise ValueError("Wyze email and password must be set via environment variables.")
    
    # Initialize the client. The SDK handles token acquisition and refreshing.
    wyze_client = Client(email=WYZE_EMAIL, password=WYZE_PASSWORD)
    print("Wyze client initialized successfully.")
    
except Exception as e:
    print(f"Error initializing Wyze client: {e}")
    wyze_client = None

# Track the last successful check time to prevent re-sending old events
# Start by checking the last 15 seconds to catch any events missed during startup
last_check_time = datetime.now(timezone.utc) - timedelta(seconds=15)

def send_to_csharp_bridge(event_data: dict):
    """Sends the event payload to the C# Web API endpoint."""
    print(f"-> Sending event to C# Bridge at {TARGET_API_URL}")
    try:
        # Construct the payload to match the C# model (DoorbellEventPayload)
        payload = {
            "eventType": event_data.get("eventType"),
            "deviceMac": event_data.get("deviceMac"),
            "eventTimeUtc": event_data.get("eventTime"), # Reusing eventTime as UTC
            "message": "Doorbell Button Pressed"
        }
        
        response = requests.post(TARGET_API_URL, json=payload, timeout=5)
        response.raise_for_status() # Raise an exception for bad status codes (4xx or 5xx)
        
        print(f"-> C# Bridge responded: Status {response.status_code}")

    except requests.exceptions.RequestException as e:
        print(f"!!! Error sending event to C# Bridge: {e}")
    except Exception as e:
        print(f"!!! An unexpected error occurred during POST: {e}")

def poll_for_doorbell_events(client: WyzeClient, device_mac: str):
    """Polls the Wyze API for events for the specified device."""
    global last_check_time
    
    current_time = datetime.now(timezone.utc)
    
    # We want events from the last check time up to the current time
    # The API expects timestamps as UTC, but the Wyze SDK might need conversion
    # We will pass datetime objects directly and let the SDK handle formatting
    start_time = last_check_time
    end_time = current_time

    print(f"\nPolling for events between {start_time.isoformat()} and {end_time.isoformat()}...")

    try:
        # Get event history for the specified device and time window
        # max_count is set to 10, assuming a low volume of events.
        events = client.events.list(
            device_mac=device_mac,
            start_time=start_time,
            end_time=end_time,
            max_count=10
        )
        
        new_doorbell_presses = []
        latest_event_time = start_time

        for event in events:
            # Check for Doorbell Button Press (event type 2005)
            # Filter the list on the client side, as the SDK documentation is sparse on event types
            if event.event_type == EVENT_TYPE_BUTTON_PRESS:
                event_data = {
                    "eventType": event.event_type,
                    "deviceMac": device_mac,
                    # Convert event time to UTC ISO format string for C#
                    "eventTime": event.event_ts.astimezone(timezone.utc).isoformat(), 
                }
                new_doorbell_presses.append(event_data)
                
            # Keep track of the latest event timestamp encountered
            if event.event_ts.astimezone(timezone.utc) > latest_event_time:
                latest_event_time = event.event_ts.astimezone(timezone.utc)


        # Process and send new events
        if new_doorbell_presses:
            print(f"!!! Found {len(new_doorbell_presses)} new doorbell press(es).")
            # Iterate and send each event individually
            for press in reversed(new_doorbell_presses): # Process oldest first
                 send_to_csharp_bridge(press)
        else:
            print("No new doorbell press events found.")
        
        # Update last_check_time to the latest event time found (or current time if no events)
        # This prevents events older than the latest processed event from being checked again
        if latest_event_time > last_check_time:
            last_check_time = latest_event_time + timedelta(seconds=0.001) # Move past the last processed timestamp
        else:
            last_check_time = current_time # Fallback to current time if no new events were found in the window


    except Exception as e:
        print(f"!!! An error occurred during Wyze API polling: {e}")
        # In case of an API error, only advance the check time by a small amount to retry the window later
        # Or, ideally, keep the time the same to retry the same window after a delay
        # last_check_time remains unchanged to re-attempt the current window next loop.
        pass

def main():
    """Main application loop."""
    if not wyze_client:
        print("Exiting due to failed Wyze client initialization.")
        return

    print(f"Starting Wyze Doorbell Event Bridge. Polling device {DOORBELL_MAC} every {POLLING_INTERVAL_SECONDS} seconds...")

    while True:
        poll_for_doorbell_events(wyze_client, DOORBELL_MAC)
        time.sleep(POLLING_INTERVAL_SECONDS)

if __name__ == "__main__":
    main()
