package main

import (
	"fmt"
	"io"
	"log"
	"net/http"
	"time"

	"github.com/MobilityData/gtfs-realtime-bindings/golang/gtfs"
	"google.golang.org/protobuf/proto"
)

const (
	// The direct Protocol Buffer path for the NextGen network
	martaUrl = "https://gtfs-rt.itsmarta.com/TMGTFSRealTimeWebService/vehicle/vehiclepositions.pb"
)

func main() {
	fmt.Println("🎹 Starting MARTA Jazz Data Stream...")

	client := &http.Client{
		Timeout: 15 * time.Second,
	}

	// 1. Initialize request
	req, err := http.NewRequest("GET", martaUrl, nil)
	if err != nil {
		log.Fatalf("Error creating request: %v", err)
	}

	// 'Accept: */*' is more resilient than 'application/x-google-protobuf'
	req.Header.Set("Accept", "*/*")

	// Adding a User-Agent is often required to prove you aren't a simple bot
	req.Header.Set("User-Agent", "MartaJazz/1.0 (Bus Realtime Project)")

	// 3. Execute Request
	resp, err := client.Do(req)
	if err != nil {
		log.Fatalf("🛰️ Connection Error: %v", err)
	}
	defer resp.Body.Close()

	// If it still fails, the error message will help us debug the subscription
	if resp.StatusCode != http.StatusOK {
		log.Fatalf("❌ Server rejected request: %s. \nNote: Verify your key is for 'Bus GTFS-rt' and not just 'Rail REST'.", resp.Status)
	}

	// 4. Read the binary body
	body, err := io.ReadAll(resp.Body)
	if err != nil {
		log.Fatal("Error reading response body:", err)
	}

	// 5. Decode the Protobuf
	feed := &gtfs.FeedMessage{}
	if err := proto.Unmarshal(body, feed); err != nil {
		log.Fatal("Error parsing protobuf. The server might have sent an HTML error page instead of binary data.")
	}

	// 6. Output for the Jazz Logic
	fmt.Printf("✅ Success! Found %d buses currently performing.\n\n", len(feed.Entity))

	for _, entity := range feed.Entity {
		if entity.Vehicle != nil {
			v := entity.Vehicle
			id := *v.Vehicle.Id
			lat := *v.Position.Latitude
			lon := *v.Position.Longitude

			fmt.Printf("Bus %s -> (%.4f, %.4f)", id, lat, lon)

			// Musical Tempo/Speed Logic
			if v.Position.Speed != nil {
				fmt.Printf(" | Speed: %.2f m/s", *v.Position.Speed)
			}
			fmt.Println()
		}
	}
}
