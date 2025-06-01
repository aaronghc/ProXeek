import os
import sys
import json
import base64
from io import BytesIO
from dotenv import load_dotenv
from langchain_core.messages import HumanMessage, SystemMessage
from langchain_openai import ChatOpenAI
from PIL import Image
import asyncio
from typing import List, Dict, Any


# Set up logging to help debug
def log(message):
    print(f"LOG: {message}")
    sys.stdout.flush()


log("Script started")

# Check if we're running from Unity or from the server
if len(sys.argv) > 1:
    # Running from server with parameters file
    log(f"Running with parameters file: {sys.argv[1]}")
    params_file = sys.argv[1]

    try:
        with open(params_file, 'r') as f:
            params = json.load(f)
        log(f"Loaded parameters")

        # Extract data from parameters
        haptic_annotation_json = params.get('hapticAnnotationJson', '')
        environment_image_base64_list = params.get('environmentImageBase64List', [])
        virtual_object_snapshots = params.get('virtualObjectSnapshots', [])
        arrangement_snapshots = params.get('arrangementSnapshots', [])

        log(f"Found {len(environment_image_base64_list)} environment images")
        log(f"Found {len(virtual_object_snapshots)} virtual object snapshots")
        log(f"Found {len(arrangement_snapshots)} arrangement snapshots")
        log(f"Haptic annotation JSON present: {'Yes' if haptic_annotation_json else 'No'}")

    except Exception as e:
        log(f"Error reading parameters file: {e}")
        haptic_annotation_json = ''
        environment_image_base64_list = []
        virtual_object_snapshots = []
        arrangement_snapshots = []
else:
    # Default when running from Unity Editor
    log("No parameters file provided, using defaults")
    haptic_annotation_json = ''
    environment_image_base64_list = []
    virtual_object_snapshots = []
    arrangement_snapshots = []

# Get the project path
script_dir = os.path.dirname(os.path.abspath(__file__))
log(f"Script directory: {script_dir}")

# Add the script directory to sys.path
if script_dir not in sys.path:
    sys.path.append(script_dir)
    log(f"Added {script_dir} to sys.path")

# Load environment variables
try:
    load_dotenv(os.path.join(script_dir, '.env'))
    log("Loaded .env file")
except Exception as e:
    log(f"Error loading .env file: {e}")

# Get API keys
api_key = os.environ.get("OPENAI_API_KEY")
langchain_api_key = os.environ.get("LANGCHAIN_API_KEY")

log(f"API key found: {'Yes' if api_key else 'No'}")
log(f"Langchain API key found: {'Yes' if langchain_api_key else 'No'}")

# If keys not found in environment, try to read directly from .env file
if not api_key or not langchain_api_key:
    try:
        log("Trying to read API keys directly from .env file")
        with open(os.path.join(script_dir, '.env'), 'r') as f:
            content = f.read()
            for line in content.split('\n'):
                if line.startswith('OPENAI_API_KEY='):
                    api_key = line.strip().split('=', 1)[1].strip('"\'')
                    log("Found OPENAI_API_KEY in .env file")
                elif line.startswith('LANGCHAIN_API_KEY='):
                    langchain_api_key = line.strip().split('=', 1)[1].strip('"\'')
                    log("Found LANGCHAIN_API_KEY in .env file")
    except Exception as e:
        log(f"Error reading .env file directly: {e}")

# Set up LangChain tracing
os.environ["LANGCHAIN_TRACING_V2"] = "true"
if langchain_api_key:
    os.environ["LANGCHAIN_API_KEY"] = langchain_api_key

# Initialize the physical object recognition LLM
physical_object_recognition_llm = ChatOpenAI(
    model="o4-mini-2025-04-16",
    temperature=0.1,
    base_url="https://api.nuwaapi.com/v1",
    api_key=api_key
)

# Initialize the virtual object processing LLM
virtual_object_processor_llm = ChatOpenAI(
    model="o4-mini-2025-04-16",
    temperature=0.1,
    base_url="https://api.nuwaapi.com/v1",
    api_key=api_key
)

# System prompt for object recognition
object_recognition_system_prompt = """
You are an expert computer vision system that identifies objects in images.

For each image, create a detailed list of all recognizable objects with the following information:

1. Its name with some details (e.g., "white cuboid airpods case")
2. Its position in the image (e.g., "bottom left of the image")

FORMAT YOUR RESPONSE AS A JSON ARRAY with the following structure:

```json
[
  {
    "object_id": 1,
    "object": "object name with some details",
    "position": "position in image"
  },
  {
    "object_id": 2,
    ...
  }
]
```

Be comprehensive and include all clearly visible objects.
"""

# System prompt for virtual object processing
virtual_object_processor_system_prompt = """
You are a haptic feedback expert who specializes in describing the expected haptic sensations when interacting with virtual objects in VR.

Your task is to analyze virtual object data and create clear descriptions of the haptic feedback that users should experience when interacting with these virtual objects. Your descriptions will be used to find appropriate physical proxies from the real environment.

For each virtual object, consider the following properties:
- objectName: The target virtual object in the VR scene
- involvementType: grasp: users are very likely to grasp the game object. contact: users are very likely to touch or contact the game object using body parts. substrate: users are unlikely to contact the game object directly; instead, they tend to use another grasped game object ot interact with it.
- description: Overall usage of this virtual object in the VR scene
- engagementLevel: How frequently users interact with the object (0: low, 1: medium, 2: high)
- inertia: Highly expected haptic feedback, if any, regarding the target virtual object's mass and gravity center.
- interactivity: Highly expected haptic feedback, if any, regarding the target virtual object's interactable features.
- outline: Highly expected haptic feedback, if any, regarding the target virtual object's shape and size.
- texture: Highly expected haptic feedback, if any, regarding the target virtual object's surface texture.
- hardness: Highly expected haptic feedback, if any, regarding the target virtual object's hardness or flexibility.
- temperature: Highly expected haptic feedback, if any, regarding the target virtual object's perceived temperature.
- inertiaValue: Importance of inertia (0-1)
- interactivityValue: Importance of interactivity (0-1)
- outlineValue: Importance of outline (0-1)
- textureValue: Importance of texture (0-1)
- hardnessValue: Importance of hardness (0-1)
- temperatureValue: Importance of temperature (0-1)
- dimensions_meters: Physical dimensions of the object

Create a comprehensive haptic feedback description that:
1. Prioritizes the properties with higher importance values
2. Describes the highlighted haptic sensations users should feel
3. Infers the most likely contact part(s) of the target virtual object

FORMAT YOUR RESPONSE AS A JSON ARRAY where each object has:
- "objectName": Name of the virtual object
- "hapticFeedback": Your comprehensive haptic feedback description

The JSON should look like:
```json
[
  {
    "objectName": "Example Object",
    "hapticFeedback": "Detailed haptic feedback description focusing on the most important properties..."
  },
  ...
]
```
"""

# Function to process a single image and recognize objects
async def process_single_image(image_base64: str, image_id: int) -> Dict[str, Any]:
    try:
        # Create the human message with image content
        human_message_content = [
            {"type": "text", "text": f"Identify all objects in this image (image ID: {image_id})."},
            {"type": "image_url", "image_url": {"url": f"data:image/jpeg;base64,{image_base64}", "detail": "high"}}
        ]
        
        # Create the messages
        messages = [
            SystemMessage(content=object_recognition_system_prompt),
            HumanMessage(content=human_message_content)
        ]
        
        # Get response from the model
        log(f"Sending image {image_id} to object recognition model")
        response = await physical_object_recognition_llm.ainvoke(messages)
        log(f"Received response for image {image_id}")
        
        # Extract JSON from response
        response_text = response.content
        # Find JSON content between ```json and ```
        json_start = response_text.find("```json")
        if json_start != -1:
            json_start += 7  # Length of ```json
            json_end = response_text.find("```", json_start)
            if json_end != -1:
                json_content = response_text[json_start:json_end].strip()
            else:
                json_content = response_text[json_start:].strip()
        else:
            # Try to find any JSON array in the response
            json_start = response_text.find("[")
            json_end = response_text.rfind("]") + 1
            if json_start != -1 and json_end > json_start:
                json_content = response_text[json_start:json_end].strip()
            else:
                json_content = response_text
        
        try:
            # Parse the JSON response
            objects = json.loads(json_content)
            
            # Add image_id to each object
            for obj in objects:
                obj["image_id"] = image_id
                
            return {"image_id": image_id, "objects": objects, "status": "success"}
            
        except json.JSONDecodeError as e:
            log(f"Error parsing JSON for image {image_id}: {e}")
            log(f"Raw content: {json_content}")
            return {"image_id": image_id, "objects": [], "status": "error", "error": str(e)}
            
    except Exception as e:
        log(f"Error processing image {image_id}: {e}")
        return {"image_id": image_id, "objects": [], "status": "error", "error": str(e)}

# Process multiple images concurrently
async def process_multiple_images(environment_images: List[str]) -> Dict[int, List[Dict]]:
    tasks = []
    for i, image_base64 in enumerate(environment_images):
        tasks.append(process_single_image(image_base64, i))
    
    results = await asyncio.gather(*tasks)
    
    # Organize results into a database
    object_database = {}
    for result in results:
        image_id = result["image_id"]
        if result["status"] == "success":
            object_database[image_id] = result["objects"]
        else:
            log(f"Processing failed for image {image_id}: {result.get('error', 'Unknown error')}")
            object_database[image_id] = []
            
    return object_database

# Function to save object database to JSON file
def save_object_database(object_db: Dict[int, List[Dict]], output_path: str) -> str:
    try:
        # Convert to serializable format
        serializable_db = {str(k): v for k, v in object_db.items()}
        
        # Ensure directory exists
        os.makedirs(os.path.dirname(output_path), exist_ok=True)
        
        # Save to file
        with open(output_path, 'w') as f:
            json.dump(serializable_db, f, indent=2)
            
        log(f"Object database saved to {output_path}")
        return output_path
    except Exception as e:
        log(f"Error saving object database: {e}")
        return None

# New function to process virtual objects and generate haptic feedback descriptions
async def process_virtual_objects(haptic_annotation_json: str) -> List[Dict]:
    if not haptic_annotation_json:
        log("No haptic annotation data provided")
        return []
    
    try:
        # Parse the haptic annotation JSON
        haptic_data = json.loads(haptic_annotation_json)
        node_annotations = haptic_data.get("nodeAnnotations", [])
        
        if not node_annotations:
            log("No node annotations found in haptic data")
            return []
        
        log(f"Found {len(node_annotations)} virtual objects in haptic annotation data")
        
        # Function to normalize object names for matching
        def normalize_name(name):
            # Convert to lowercase, replace spaces with underscores, remove special characters
            return name.lower().replace(" ", "_").replace("-", "_")
        
        # Create a map of normalized object name to snapshot for flexible lookup
        object_snapshot_map = {}
        normalized_name_map = {}  # Maps normalized names back to original names
        
        # First, create a map of normalized names to original snapshots
        for snapshot in virtual_object_snapshots:
            if 'objectName' in snapshot and 'imageBase64' in snapshot:
                original_name = snapshot['objectName']
                normalized_name = normalize_name(original_name)
                object_snapshot_map[normalized_name] = snapshot['imageBase64']
                normalized_name_map[normalized_name] = original_name
                # Also add the original name for direct matches
                object_snapshot_map[original_name] = snapshot['imageBase64']
        
        log(f"Found {len(object_snapshot_map)} virtual object snapshots")
        log(f"Normalized names: {list(normalized_name_map.keys())}")
        
        # Build the human message content with objects and their snapshots
        human_message_content = []
        
        # Add introduction text
        human_message_content.append({
            "type": "text", 
            "text": "Please analyze the following virtual objects and create detailed haptic feedback descriptions for each. Focus on the properties with higher importance values (those with higher *Value numbers)."
        })
        
        # Process each virtual object one by one
        for node in node_annotations:
            object_name = node.get("objectName", "Unknown Object")
            normalized_object_name = normalize_name(object_name)
            
            # Add object's annotation data as JSON
            object_json = json.dumps(node, indent=2)
            object_text = f"\n\n## Virtual Object: {object_name}\n```json\n{object_json}\n```"
            
            # Add text content for this object
            human_message_content.append({
                "type": "text",
                "text": object_text
            })
            
            # Try to find snapshot with various name formats
            snapshot_found = False
            
            # First try direct match
            if object_name in object_snapshot_map:
                log(f"Found snapshot for {object_name} (direct match)")
                human_message_content.append({
                    "type": "image_url", 
                    "image_url": {
                        "url": f"data:image/jpeg;base64,{object_snapshot_map[object_name]}", 
                        "detail": "high"
                    }
                })
                snapshot_found = True
            # Then try normalized match
            elif normalized_object_name in object_snapshot_map:
                log(f"Found snapshot for {object_name} (normalized as {normalized_object_name})")
                human_message_content.append({
                    "type": "image_url", 
                    "image_url": {
                        "url": f"data:image/jpeg;base64,{object_snapshot_map[normalized_object_name]}", 
                        "detail": "high"
                    }
                })
                snapshot_found = True
            # Finally try a fuzzy match
            else:
                # Try to find partial matches
                potential_matches = [norm_name for norm_name in normalized_name_map.keys() 
                                    if normalized_object_name in norm_name or norm_name in normalized_object_name]
                
                if potential_matches:
                    best_match = potential_matches[0]  # Take the first match
                    log(f"Found snapshot for {object_name} (fuzzy match: {normalized_name_map[best_match]})")
                    human_message_content.append({
                        "type": "image_url", 
                        "image_url": {
                            "url": f"data:image/jpeg;base64,{object_snapshot_map[best_match]}", 
                            "detail": "high"
                        }
                    })
                    snapshot_found = True
            
            if not snapshot_found:
                log(f"No snapshot found for {object_name} (normalized: {normalized_object_name})")
        
        # Add final instruction
        human_message_content.append({
            "type": "text",
            "text": "\nDescribe what makes a good physical proxy for each object based on its haptic properties."
        })
        
        # Create the messages
        messages = [
            SystemMessage(content=virtual_object_processor_system_prompt),
            HumanMessage(content=human_message_content)
        ]
        
        # Get response from the model
        log("Sending virtual object data to LLM for haptic feedback processing")
        response = await virtual_object_processor_llm.ainvoke(messages)
        log("Received haptic feedback descriptions")
        
        # Extract JSON from response using a more robust approach
        response_text = response.content
        
        # First try to find JSON between code blocks
        json_start = response_text.find("```json")
        if json_start != -1:
            json_start += 7  # Length of ```json
            json_end = response_text.find("```", json_start)
            if json_end != -1:
                json_content = response_text[json_start:json_end].strip()
            else:
                json_content = response_text[json_start:].strip()
        else:
            # Try to find JSON array directly
            json_start = response_text.find("[")
            json_end = response_text.rfind("]") + 1
            if json_start != -1 and json_end > json_start:
                json_content = response_text[json_start:json_end].strip()
            else:
                # As a fallback, try to use the entire response
                json_content = response_text
        
        try:
            # Parse the JSON response
            haptic_feedback_data = json.loads(json_content)
            
            # Create a mapping from object name to haptic feedback
            haptic_feedback_map = {item["objectName"]: item["hapticFeedback"] for item in haptic_feedback_data}
            
            # Merge the original node annotations with the haptic feedback descriptions
            enhanced_node_annotations = []
            for node in node_annotations:
                object_name = node["objectName"]
                enhanced_node = node.copy()
                enhanced_node["hapticFeedback"] = haptic_feedback_map.get(object_name, "No haptic feedback description available")
                enhanced_node_annotations.append(enhanced_node)
            
            return enhanced_node_annotations
            
        except json.JSONDecodeError as e:
            log(f"Error parsing haptic feedback JSON: {e}")
            log(f"Raw content: {json_content}")
            
            # Return the original node annotations without haptic feedback as a fallback
            return [node.copy() for node in node_annotations]
            
    except Exception as e:
        log(f"Error processing virtual objects: {e}")
        import traceback
        log(traceback.format_exc())
        return []

try:
    # Create a variable to store the processing results
    result = {"status": "success", "message": "Processing complete"}
    
    # Define a function to run both tasks concurrently
    async def run_concurrent_tasks():
        tasks = []
        results = {}
        
        # Add physical object task if we have environment images
        if environment_image_base64_list:
            log(f"Setting up task to process {len(environment_image_base64_list)} environment images")
            physical_task = process_multiple_images(environment_image_base64_list)
            tasks.append(physical_task)
        
        # Add virtual object task if we have haptic annotation data
        if haptic_annotation_json:
            log("Setting up task to process virtual objects from haptic annotation data")
            virtual_task = process_virtual_objects(haptic_annotation_json)
            tasks.append(virtual_task)
        
        # Run both tasks concurrently and get results
        if tasks:
            log("Starting concurrent processing of physical and virtual objects")
            task_results = await asyncio.gather(*tasks)
            
            # Process results
            task_index = 0
            
            # Handle physical objects result if that task was included
            if environment_image_base64_list:
                physical_result = task_results[task_index]
                task_index += 1
                results["physical_result"] = physical_result
            
            # Handle virtual objects result if that task was included
            if haptic_annotation_json:
                virtual_result = task_results[task_index]
                results["virtual_result"] = virtual_result
        
        return results
    
    # Run all tasks concurrently in a single event loop
    concurrent_results = asyncio.run(run_concurrent_tasks())
    
    # Process physical objects results if available
    if environment_image_base64_list:
        log("Processing completed physical object detection results")
        physical_object_database = concurrent_results.get("physical_result", {})
        
        # Save physical object database
        output_dir = os.path.join(script_dir, "output")
        physical_output_path = os.path.join(output_dir, "physical_object_database.json")
        physical_saved_path = save_object_database(physical_object_database, physical_output_path)
        
        # Calculate total objects found
        total_physical_objects = sum(len(objects) for objects in physical_object_database.values())
        log(f"Physical object recognition complete. Found {total_physical_objects} objects across {len(physical_object_database)} images.")
        
        # Add to result
        result["physical_objects"] = {
            "count": total_physical_objects,
            "database_path": physical_saved_path,
            "object_database": physical_object_database
        }
    else:
        log("No environment images to process")
        result["physical_objects"] = {"status": "error", "message": "No environment images provided"}
    
    # Process virtual objects results if available
    if haptic_annotation_json:
        log("Processing completed virtual object processing results")
        enhanced_virtual_objects = concurrent_results.get("virtual_result", [])
        
        # Save virtual object database
        output_dir = os.path.join(script_dir, "output")
        virtual_output_path = os.path.join(output_dir, "virtual_object_database.json")
        
        # Ensure output directory exists
        os.makedirs(output_dir, exist_ok=True)
        
        # Save virtual object database
        with open(virtual_output_path, 'w') as f:
            json.dump(enhanced_virtual_objects, f, indent=2)
        
        log(f"Virtual object processing complete. Enhanced {len(enhanced_virtual_objects)} virtual objects with haptic feedback descriptions.")
        
        # Add to result
        result["virtual_objects"] = {
            "count": len(enhanced_virtual_objects),
            "database_path": virtual_output_path,
            "object_database": enhanced_virtual_objects
        }
    else:
        log("No haptic annotation data to process")
        result["virtual_objects"] = {"status": "error", "message": "No haptic annotation data provided"}
    
    # Print final result as JSON
    print(json.dumps(result, indent=2))
        
except Exception as e:
    log(f"Error in processing: {e}")
    import traceback
    log(traceback.format_exc())
    print(json.dumps({"status": "error", "message": str(e)}))