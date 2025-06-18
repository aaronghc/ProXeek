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

# Helper function to normalize object names for matching
def normalize_name(name):
    # Convert to lowercase, replace spaces with underscores, remove special characters
    return name.lower().replace(" ", "_").replace("-", "_")

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
    log("LangSmith tracing enabled with API key")
else:
    log("Warning: LangSmith API key not found - tracing may not work properly")

# o4-mini-2025-04-16
# Initialize the physical object recognition LLM
physical_object_recognition_llm = ChatOpenAI(
    model="gpt-4o-mini",
    temperature=0.1,
    base_url="https://api.nuwaapi.com/v1",
    api_key=api_key
)

# Initialize the virtual object processing LLM
virtual_object_processor_llm = ChatOpenAI(
    model="gpt-4o-mini",
    temperature=0.1,
    base_url="https://api.nuwaapi.com/v1",
    api_key=api_key
)

# Initialize the proxy matching LLM
proxy_matching_llm = ChatOpenAI(
    model="gpt-4o-mini",
    temperature=0.1,
    base_url="https://api.nuwaapi.com/v1",
    api_key=api_key
)

# Initialize the property rating LLM
property_rating_llm = ChatOpenAI(
    model="gpt-4o-mini",
    temperature=0.1,
    base_url="https://api.nuwaapi.com/v1",
    api_key=api_key
)

# Initialize the relationship rating LLM
relationship_rating_llm = ChatOpenAI(
    model="gpt-4o-mini",
    temperature=0.1,
    base_url="https://api.nuwaapi.com/v1",
    api_key=api_key
)
log("Initialized relationship_rating_llm for LangSmith tracing")

# Initialize the substrate utilization LLM
substrate_utilization_llm = ChatOpenAI(
    model="gpt-4o-mini",
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
You are an expert in user interaction analysis who specializes in understanding how users physically interact with virtual objects in VR.

Your task is to analyze virtual object metadata and its snapshot, and deduce how users would likely interact with these virtual objects based on the annotations. This will help identify appropriate physical proxies from the real environment.

For each virtual object, consider the following information:
- objectName: The target virtual object in the VR scene
- involvementType: grasp: objects that users hold and manipulate. contact: objects that users touch/hit/push with body parts. substrate: objects that users interact with indirectly using other objects.
- usage: Overall usage of this virtual object in the VR scene
- inertia: Highly expected haptic feedback, if any, regarding the target virtual object's mass, weight distribution, and resistance to movement.
- interactivity: Highly expected haptic feedback, if any, regarding how the virtual object responds to user actions.
- outline: Highly expected haptic feedback, if any, regarding the target virtual object's shape and size.
- texture: Highly expected haptic feedback, if any, regarding the target virtual object's surface feel and tactile patterns.
- hardness: Highly expected haptic feedback, if any, regarding the target virtual object's rigidity, compliance, and deformation.
- temperature: Highly expected haptic feedback, if any, regarding the target virtual object's thermal properties and heat transfer.
- dimensions_meters: Physical dimensions of the object

Consider these deduction strategies based on involvement type:

For grasp objects:
- Identify the grip type (power grip, precision grip, pinch, etc.)
- Consider manipulation patterns (swing, rotate, squeeze, trigger, etc.)
- Analyze tool-like behaviors (pointing, striking, cutting motions, etc.)

For contact objects:
- Identify contact method (press, push, strike, slide, etc.)
- Determine body part involved (finger, palm, fist, foot, etc.)
- Consider interaction duration (momentary tap vs. sustained contact)

Create a comprehensive interaction deduction that:
1. Identifies the specific way users would interact with the object
2. Describes the physical movements and contact patterns
3. Explains how the object would respond to user interaction

FORMAT YOUR RESPONSE AS A JSON ARRAY where each object has:
- "objectName": Name of the virtual object
- "interactionDeduction": Your comprehensive interaction deduction

The JSON should look like:
```json
[
  {
    "objectName": "Example Object",
    "interactionDeduction": "Detailed description of how users would interact with this object..."
  },
  ...
]
```
"""

# System prompt for proxy matching
proxy_matching_system_prompt = """
You are an expert in haptic design who specializes in finding physical proxies for virtual objects in VR.

Your task is to analyze ONE virtual object and evaluate ALL physical objects from the environment as potential haptic proxies.

First, carefully consider the deduced interaction for the virtual object—how users are expected to interact with it

For each physical object, propose a specific method to utilize it as a haptic proxy that best replicates the deduced interaction of the virtual object.

Focus on matching the most important haptic properties of the virtual object (those with higher importance values), but always ensure your proxy method enables the user to perform the same type of interaction as described in the interaction deduction.

Make sure to include the object_id and image_id for each physical object exactly as they appear in the detected objects list.

IMPORTANT: Image IDs begin at 0 (not 1). The first image has image_id=0, the second has image_id=1, etc.
"""

# Function to generate property-specific system prompt
def get_property_rating_system_prompt(property_name):
    property_type = property_name.replace("Value", "")
    
    # Base prompt
    base_prompt = f"""
You are an expert in haptic design who specializes in evaluating how well physical objects can deliver the expected haptic feedback for virtual objects in VR.

Your task is to evaluate how well each physical object can provide the expected {property_type} haptic feedback described for the virtual object. Focus on the specific haptic description provided rather than making general assumptions about the property type.

Rate each physical object on a 7-point Likert scale based on how well it can deliver the described {property_type} experience:
1 - Strongly Disagree 
2 - Disagree
3 - Somewhat Disagree
4 - Neutral
5 - Somewhat Agree
6 - Agree
7 - Strongly Agree

Use the following rubric to guide your evaluation:
"""

    # Property-specific rubrics
    rubrics = {
        "inertia": """
Inertia:
- 1-Strong Disagree
  - The weight difference is immediately and jarringly noticeable upon first contact
  - Center of mass feels completely misaligned (e.g., top-heavy physical object for a bottom-heavy virtual object)
  - Movement resistance feels entirely wrong (e.g., extremely light physical plastic bottle for a heavy virtual sledgehammer)
- 7-Strong Agree
  - Weight feels natural as expected throughout the entire interaction
  - Center of mass location allows intuitive and stable manipulation
  - Movement resistance and momentum feel completely consistent with the virtual object
""",
        "interactivity": """
Interactivity:
- 1-Strong Disagree
  - Required interactive elements are completely absent or non-functional
  - User cannot perform the intended actions at all
- 7-Strong Agree
  - All interactive elements are present and function intuitively as expected
  - Degrees of freedom match exactly (rotation axes, sliding directions, button positions)
""",
        "outline": """
Outline:
- 1-Strong Disagree
  - Size mismatch is immediately apparent and disrupts grip formation
  - Basic shape category is entirely different (e.g., spherical physical object for a virtual tetrahedron)
  - Key affordances or contact points are absent
- 7-Strong Agree
  - Size and proportions feel completely natural in the hand
  - Shape affords all expected grips and manipulation patterns
""",
        "texture": """
Texture:
- 1-Strong Disagree
  - Surface finishing is shockingly different from expectations (e.g., extremely rough physical surface for virtual polished glass)
  - Tactile landmarks are missing or misplaced
- 7-Strong Agree
  - Surface texture feels exactly as anticipated
  - Texture transitions occur at expected locations
""",
        "hardness": """
Hardness:
- 1-Strong Disagree
  - Compliance is completely wrong, it affects basic interaction (e.g., soft foam for a virtual metal tool)
  - Deformation behavior is shocking and breaks immersion
- 7-Strong Agree
  - Material hardness feels precisely as expected
  - Deformation behavior matches material expectations perfectly
""",
        "temperature": """
Temperature:
- 1-Strong Disagree
  - Temperature sensation is shockingly wrong or opposite to expectations (e.g., warm/hot physical object for virtual ice cube)
  - Thermal conductivity creates wrong sensations (e.g., insulating material for a virtual metal object)
- 7-Strong Agree
  - Initial temperature matches the expected thermal sensation
  - Heat flow during contact feels natural for the material type
"""
    }
    
    # Output format
    output_format = """
FORMAT YOUR RESPONSE AS A JSON ARRAY with the following structure:
```json
[
  {
    "virtualObject": "name of the virtual object",
    "property": "name of the property being evaluated",
    "physicalObject": "name of the physical object",
    "object_id": 1,
    "image_id": 0,
    "rating": 5,
    "explanation": "Brief explanation of why this rating was given"
  },
  ...
]
```

Make sure to include ALL physical objects in your evaluation, even those with low ratings.
"""
    
    # Construct the complete prompt with only the relevant property rubric
    full_prompt = base_prompt + rubrics.get(property_type.lower(), "") + output_format
    return full_prompt

# System prompt for relationship rating
relationship_rating_system_prompt = """
You are an expert in haptic design who specializes in evaluating how well pairs of physical objects can simulate the expected haptic feedback when two virtual objects interact with each other in VR.

You will be provided with pre-generated substrate utilization methods for each contact-substrate pair. Your task is to critically evaluate how well each pair can deliver the expected haptic feedback, considering both the contact object's utilization method and the provided substrate utilization method.

Rate each physical object pair on a 7-point Likert scale for the following three aspects:
1 - Strongly Disagree 
2 - Disagree
3 - Somewhat Disagree
4 - Neutral
5 - Somewhat Agree
6 - Agree
7 - Strongly Agree

Evaluate each pair based on these three questions:
1. "I felt the haptic feedback was well coordinated with visual feedback" - Harmony: Focus on the synchronization of contact-substrate contact
2. "I felt the contact object effectively conveyed substrate properties and interaction variations through my hand" - Expressivity: How well substrate properties are conveyed through the contact object
3. "I felt using this physical contact object on this physical substrate closely simulated the intended haptic feedback"- Realism: How well the overall contact-substrate interaction matches the expected haptic experience

Use the following rubrics to guide your evaluation:

**Harmony Dimension**

Score 1 - Strongly Disagree:
- Physical contact happens noticeably before visual contact or visual contact occurs with no physical sensation
- Force direction contradicts visual motion
- Visual substrate responses don't match felt impact intensity

Score 7 - Strongly Agree:
- Physical and visual contact perfectly synchronized
- Every visual contact event has corresponding haptic feedback
- Force vectors align naturally with visual physics
- Substrate visual behavior matches haptic intensity

**Expressivity Dimension**

Score 1 - Strongly Disagree:
- Single uniform feedback regardless of interaction parameters
- No variation with impact speed, angle, or force

Score 7 - Strongly Agree:
- Rich feedback variations convey substrate properties clearly
- Natural variations in speed, angle, and force produce different haptic responses

**Realism Dimension**

Score 1 - Strongly Disagree:
- The interaction feels fundamentally wrong (e.g., soft bouncy feedback when hammering should feel solid)
- Missing essential haptic elements that define in the expected haptic feedback

Score 7 - Strongly Agree:
- All characteristic sensations of the real interaction are present (impacts, resistance, texture transmission, etc.)
- The physical pairing naturally affords the same manipulation techniques as the virtual scenario

FORMAT YOUR RESPONSE AS A JSON ARRAY with the following structure:
```json
[
  {
    "virtualContactObject": "name of virtual contact object",
    "virtualSubstrateObject": "name of virtual substrate object", 
    "physicalContactObject": "name of physical contact object",
    "physicalSubstrateObject": "name of physical substrate object",
    "contactObject_id": 1,
    "contactImage_id": 0,
    "substrateObject_id": 2,
    "substrateImage_id": 1,
    "contactUtilizationMethod": "utilization method for the contact object",
    "substrateUtilizationMethod": "your planned utilization method for the substrate object",
    "harmony_rating": 5,
    "harmony_explanation": "Brief explanation for harmony rating considering both utilization methods",
    "expressivity_rating": 4,
    "expressivity_explanation": "Brief explanation for expressivity rating considering both utilization methods", 
    "realism_rating": 6,
    "realism_explanation": "Brief explanation for realism rating considering both utilization methods"
  },
  ...
]
```

Include EVERY pair in your evaluation.
"""

# System prompt for substrate utilization method generation
substrate_utilization_system_prompt = """
You are an expert in haptic design who specializes in determining how physical objects can be utilized as substrates to simulate virtual substrate objects in VR.

Your task is to analyze ONE specific virtual contact-substrate relationship and determine how each physical object in the environment could be utilized as a substrate to work with a specific physical contact object.

You will be given:
1. A virtual contact object and its corresponding physical contact object with its utilization method
2. A virtual substrate object and the expected haptic feedback for their interaction
3. All physical objects in the environment as potential substrate candidates

For each physical substrate candidate, determine:
- How it should be positioned, oriented, or prepared
- What specific properties or features should be utilized
- How it would interact with the given physical contact object's utilization method
- What modifications or setup might be needed

Use the following evaluation guidelines when planning substrate utilization methods:

**Harmony Considerations:**
- Ensure the substrate setup allows for synchronized physical and visual contact
- Consider how the substrate positioning affects force direction alignment
- Plan for substrate responses that match expected visual behavior

**Expressivity Considerations:**
- Think about how the substrate can provide varied feedback based on interaction parameters
- Consider how different contact speeds, angles, or forces would affect the substrate response
- Plan for substrate properties that can be effectively conveyed through the contact object

**Realism Considerations:**
- Focus on substrate utilization that delivers the essential haptic sensations described in the expected feedback
- Ensure the substrate setup naturally affords the intended manipulation techniques
- Consider how well the substrate can simulate the virtual substrate's key properties

Important: Consider the specific physical contact object's utilization method when planning how each substrate should be utilized. The substrate utilization should work harmoniously with the contact object's intended use.

FORMAT YOUR RESPONSE AS A JSON ARRAY with the following structure:
```json
[
  {
    "virtualContactObject": "name of virtual contact object",
    "virtualSubstrateObject": "name of virtual substrate object",
    "physicalContactObject": "name of physical contact object",
    "contactObject_id": 1,
    "contactImage_id": 0,
    "contactUtilizationMethod": "utilization method for the contact object",
    "expectedHapticFeedback": "expected haptic feedback for the interaction",
    "physicalSubstrateObject": "name of physical substrate object",
    "substrateObject_id": 2,
    "substrateImage_id": 1,
    "substrateUtilizationMethod": "detailed method to utilize this object as a substrate for the given contact object"
  },
  ...
]
```

Include ALL physical objects as potential substrate candidates in your evaluation.
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

# New function to process virtual objects and generate deduced interaction patterns
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
        # human_message_content.append({
        #     "type": "text", 
        #     "text": "Please analyze the following virtual objects and create detailed haptic feedback descriptions for each. Focus on the properties with higher importance values (those with higher *Value numbers)."
        # })
        
        # Process each virtual object one by one, but only grasp and contact objects
        for node in node_annotations:
            object_name = node.get("objectName", "Unknown Object")
            involvement_type = node.get("involvementType", "")
            
            # Skip substrate objects - only process grasp and contact objects
            if involvement_type not in ["grasp", "contact"]:
                log(f"Skipping {object_name} with involvementType: {involvement_type}")
                continue
                
            normalized_object_name = normalize_name(object_name)
            
            # Create filtered object data with only the fields mentioned in system prompt
            filtered_node = {
                "objectName": node.get("objectName", ""),
                "involvementType": node.get("involvementType", ""),
                "usage": node.get("usage", ""),
                "inertia": node.get("inertia", ""),
                "interactivity": node.get("interactivity", ""),
                "outline": node.get("outline", ""),
                "texture": node.get("texture", ""),
                "hardness": node.get("hardness", ""),
                "temperature": node.get("temperature", ""),
                "dimensions_meters": node.get("dimensions_meters", {})
            }
            
            # Add object's filtered annotation data as JSON
            object_json = json.dumps(filtered_node, indent=2)
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
        # human_message_content.append({
        #     "type": "text",
        #     "text": "\nDescribe what makes a good physical proxy for each object based on its haptic properties."
        # })
        
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
            haptic_feedback_map = {item["objectName"]: item["interactionDeduction"] for item in haptic_feedback_data}
            
            # Merge the original node annotations with the haptic feedback descriptions
            # Only include grasp and contact objects
            enhanced_node_annotations = []
            for node in node_annotations:
                involvement_type = node.get("involvementType", "")
                
                # Skip substrate objects - only include grasp and contact objects
                if involvement_type not in ["grasp", "contact"]:
                    continue
                    
                object_name = node["objectName"]
                enhanced_node = node.copy()
                enhanced_node["interactionDeduction"] = haptic_feedback_map.get(object_name, "No interaction deduction available")
                enhanced_node_annotations.append(enhanced_node)
            
            return enhanced_node_annotations
            
        except json.JSONDecodeError as e:
            log(f"Error parsing haptic feedback JSON: {e}")
            log(f"Raw content: {json_content}")
            
            # Return the original node annotations without haptic feedback as a fallback
            # Only include grasp and contact objects
            return [node.copy() for node in node_annotations 
                    if node.get("involvementType", "") in ["grasp", "contact"]]
            
    except Exception as e:
        log(f"Error processing virtual objects: {e}")
        import traceback
        log(traceback.format_exc())
        return []

# Function to match a single virtual object with physical objects
async def match_single_virtual_object(virtual_object, environment_images, physical_object_database, object_snapshot_map):
    try:
        virtual_object_name = virtual_object.get("objectName", "Unknown Object")
        log(f"Matching proxies for virtual object: {virtual_object_name}")
        
        # Build the human message content
        human_message_content = []
        
        # 1. Add the virtual object information and haptic feedback
        virtual_object_text = f"""# Virtual Object to Evaluate: {virtual_object_name}

## Haptic Properties
```json
{json.dumps(virtual_object, indent=2)}
```

"""
        human_message_content.append({
            "type": "text", 
            "text": virtual_object_text
        })
        
        # 2. Add virtual object snapshot if available
        normalized_object_name = normalize_name(virtual_object_name)
        snapshot_found = False
        
        if virtual_object_name in object_snapshot_map:
            log(f"Adding snapshot for virtual object: {virtual_object_name}")
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{object_snapshot_map[virtual_object_name]}", 
                    "detail": "high"
                }
            })
            snapshot_found = True
        elif normalized_object_name in object_snapshot_map:
            log(f"Adding snapshot for virtual object: {virtual_object_name} (normalized)")
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{object_snapshot_map[normalized_object_name]}", 
                    "detail": "high"
                }
            })
            snapshot_found = True
            
        # 3. Add introduction to physical environment
        human_message_content.append({
            "type": "text", 
            "text": "\n# Physical Environment\nBelow are snapshots of the physical environment with detected objects that could serve as haptic proxies:"
        })
        
        # Pre-check if we actually have objects in the database to avoid "no objects detected" message
        total_objects = sum(len(objects) for objects in physical_object_database.values())
        log(f"Preparing to display {total_objects} physical objects from database")
        
        # 4. Add environment snapshots with their detected objects
        for i, image_base64 in enumerate(environment_images):
            # Add the environment snapshot
            human_message_content.append({
                "type": "text", 
                "text": f"\n## Environment Snapshot {i+1}\n"
            })
            
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{image_base64}", 
                    "detail": "high"
                }
            })
            
            # Add the detected objects for this snapshot
            objects_in_snapshot = physical_object_database.get(str(i), [])
            objects_text = "\n### Detected Objects in this Snapshot\n"
            
            if objects_in_snapshot:
                for obj in objects_in_snapshot:
                    objects_text += f"- Object ID {obj['object_id']}: {obj['object']} ({obj['position']})\n"
                    objects_text += f"  Image ID: {i}\n"
                    
                    # Add all utilization methods for this physical object (from all virtual objects)
                    util_methods_added = False
                    # Debug logging
                    log(f"Checking utilization methods for obj_id: {obj['object_id']}, img_id: {i}")
                    log(f"Number of proxy results to check: {len(proxy_matching_results)}")
                    
                    # Convert object_id to both string and int for flexible comparison
                    obj_id_int = obj['object_id']
                    if isinstance(obj_id_int, str):
                        try:
                            obj_id_int = int(obj_id_int)
                        except ValueError:
                            pass
                    
                    obj_id_str = str(obj['object_id'])
                    
                    for proxy_result in proxy_matching_results:
                        # Get proxy object_id in both formats for comparison
                        proxy_obj_id = proxy_result.get('object_id')
                        proxy_obj_id_str = str(proxy_obj_id) if proxy_obj_id is not None else None
                        
                        # Get proxy image_id in both formats for comparison
                        proxy_img_id = proxy_result.get('image_id')
                        proxy_img_id_int = proxy_img_id
                        if isinstance(proxy_img_id, str):
                            try:
                                proxy_img_id_int = int(proxy_img_id)
                            except ValueError:
                                pass
                        
                        # More flexible comparison with multiple type checks
                        if ((proxy_obj_id == obj['object_id'] or proxy_obj_id == obj_id_int or proxy_obj_id_str == obj_id_str) and
                            (proxy_img_id == i or proxy_img_id_int == i)):
                            util_method = proxy_result.get("utilizationMethod", "")
                            matched_virtual = proxy_result.get("virtualObject", "Unknown")
                            if util_method:
                                log(f"Found utilization method for {matched_virtual}, obj_id: {proxy_obj_id}, img_id: {proxy_img_id}")
                                objects_text += f"  Utilization Method for {matched_virtual}: {util_method}\n"
                                util_methods_added = True
                            else:
                                log(f"Found proxy result for {matched_virtual} but no utilization method")
                    
                    if not util_methods_added:
                        objects_text += f"  No utilization methods available for this object\n"
                        log(f"No utilization methods found for obj_id: {obj['object_id']}, img_id: {i}")
            else:
                # Check if we should look for objects in a different format (in case image_id is stored as integer keys)
                objects_in_snapshot = physical_object_database.get(i, [])
                if objects_in_snapshot:
                    for obj in objects_in_snapshot:
                        objects_text += f"- Object ID {obj['object_id']}: {obj['object']} ({obj['position']})\n"
                        # Also display the correct image_id to ensure consistency
                        objects_text += f"  Image ID: {i}\n"
                else:
                    # Last attempt - try to find objects that have this image_id in their properties
                    matching_objects = []
                    for img_id, objects_list in physical_object_database.items():
                        for obj in objects_list:
                            if obj.get("image_id") == i:
                                matching_objects.append(obj)
                    
                    if matching_objects:
                        for obj in matching_objects:
                            objects_text += f"- Object ID {obj['object_id']}: {obj['object']} ({obj['position']})\n"
                            # Also display the correct image_id to ensure consistency
                            objects_text += f"  Image ID: {i}\n"
                    else:
                        objects_text += "- No objects detected in this snapshot\n"
                
            human_message_content.append({
                "type": "text", 
                "text": objects_text
            })
        
        # 5. Add final instructions
        human_message_content.append({
            "type": "text", 
            "text": """
# Your Task

1. Evaluate EACH physical object as a potential haptic proxy for the virtual object.
2. For EACH physical object, propose a specific method to utilize it as a haptic proxy.

FORMAT YOUR RESPONSE AS A JSON ARRAY with objects having the following structure:

```json
[
  {
    "virtualObject": "name of the virtual object",
    "physicalObject": "name of the physical object",
    "object_id": 1,
    "image_id": 0,
    "proxyLocation": "location of the physical object in the environment",
    "utilizationMethod": "detailed method to use this object as a proxy"
  },
  ...
]
```

IMPORTANT: Make sure to use the EXACT image_id values shown above for each object.
Include ALL physical objects in your evaluation.
"""
        })
        
        # Create the messages
        messages = [
            SystemMessage(content=proxy_matching_system_prompt),
            HumanMessage(content=human_message_content)
        ]
        
        # Get response from the model
        log(f"Sending proxy matching request for {virtual_object_name}")
        response = await proxy_matching_llm.ainvoke(messages)
        log(f"Received method proposals for {virtual_object_name}")
        
        # Extract JSON from response
        response_text = response.content
        
        # Try to find JSON array
        json_start = response_text.find("[")
        json_end = response_text.rfind("]") + 1
        if json_start != -1 and json_end > json_start:
            json_content = response_text[json_start:json_end]
        else:
            # Try to find JSON between code blocks
            json_start = response_text.find("```json")
            if json_start != -1:
                json_start += 7  # Length of ```json
                json_end = response_text.find("```", json_start)
                if json_end != -1:
                    json_content = response_text[json_start:json_end].strip()
                else:
                    json_content = response_text[json_start:].strip()
            else:
                # As a fallback, use the entire response
                json_content = response_text
        
        try:
            # Parse the JSON response
            matching_results = json.loads(json_content)
            
            # Convert any "imageId" keys to "image_id" right away
            matching_results = rename_key_in_json(matching_results, "imageId", "image_id")
            
            # Add the original virtual object info to each result
            for result in matching_results:
                result["virtualObjectInfo"] = virtual_object
                
                # Make sure we have object_id and image_id in the result
                if "object_id" not in result:
                    log(f"Missing object_id in result for {result.get('physicalObject', 'unknown object')}")
                    # Try to find the object in the database
                    img_id = result.get("image_id")
                    phys_obj = result.get("physicalObject")
                    if img_id is not None and phys_obj:
                        img_id_str = str(img_id)
                        objects_in_img = physical_object_database.get(img_id_str, [])
                        for obj in objects_in_img:
                            if obj["object"] == phys_obj:
                                result["object_id"] = obj["object_id"]
                                log(f"Found object_id {obj['object_id']} for {phys_obj}")
                                break
                
                # Ensure consistent property types
                if "object_id" in result and isinstance(result["object_id"], str):
                    try:
                        result["object_id"] = int(result["object_id"])
                    except ValueError:
                        pass
                
                if "image_id" in result and isinstance(result["image_id"], str):
                    try:
                        result["image_id"] = int(result["image_id"])
                    except ValueError:
                        pass
                
                # Double check that image_id matches the database - fix any +1 offset
                img_id = result.get("image_id")
                if img_id is not None and isinstance(img_id, int) and img_id > 0:
                    obj_id = result.get("object_id")
                    phys_obj = result.get("physicalObject")
                    
                    # Check if this is incorrectly offset
                    correct_img_id = img_id - 1
                    img_id_str = str(correct_img_id)
                    
                    # Look in the database for a matching object at the offset-fixed image_id
                    found_match = False
                    if img_id_str in physical_object_database:
                        for obj in physical_object_database[img_id_str]:
                            if (obj_id is not None and obj.get("object_id") == obj_id) or obj.get("object") == phys_obj:
                                # Set the correct image_id
                                result["image_id"] = correct_img_id
                                log(f"Fixed image_id offset: was {img_id}, now {correct_img_id}")
                                found_match = True
                                break
                    
                    # If no matching object was found with the offset, leave the image_id as is
                    if not found_match:
                        log(f"Could not find matching object for offset correction: {phys_obj} (ID: {obj_id}, Image: {img_id})")
                
                # Make sure the physical object properties are from the database
                img_id = result.get("image_id")
                obj_id = result.get("object_id")
                
                if img_id is not None and obj_id is not None:
                    img_id_str = str(img_id)
                    objects_in_img = physical_object_database.get(img_id_str, [])
                    for obj in objects_in_img:
                        if obj.get("object_id") == obj_id:
                            # Use the database values for consistency
                            result["physicalObject"] = obj["object"]
                            result["proxyLocation"] = obj["position"]
                            break
            
            return matching_results
            
        except json.JSONDecodeError as e:
            log(f"Error parsing proxy matching JSON for {virtual_object_name}: {e}")
            log(f"Raw content: {json_content}")
            
            # Return a basic result with the error
            return [{
                "virtualObject": virtual_object_name,
                "error": f"Failed to parse response: {str(e)}",
                "rawResponse": response_text[:500]  # First 500 chars
            }]
            
    except Exception as e:
        log(f"Error in proxy matching for {virtual_object.get('objectName', 'unknown')}: {e}")
        import traceback
        log(traceback.format_exc())
        
        # Return a basic result with the error
        return [{
            "virtualObject": virtual_object.get("objectName", "unknown"),
            "error": f"Processing error: {str(e)}"
        }]

# Function to run proxy matching for all virtual objects in parallel
async def run_proxy_matching(virtual_objects, environment_images, physical_object_database, object_snapshot_map):
    tasks = []
    for virtual_object in virtual_objects:
        task = match_single_virtual_object(
            virtual_object, 
            environment_images, 
            physical_object_database, 
            object_snapshot_map
        )
        tasks.append(task)
    
    # Run all tasks concurrently
    results = await asyncio.gather(*tasks, return_exceptions=True)
    
    # Process results - flatten the array of arrays
    all_matching_results = []
    for i, result in enumerate(results):
        if isinstance(result, Exception):
            log(f"Error in proxy matching for object {i}: {result}")
            # Create fallback entry
            fallback_entry = {
                "virtualObject": virtual_objects[i].get("objectName", f"Object {i}"),
                "error": f"Task error: {str(result)}"
            }
            all_matching_results.append(fallback_entry)
        else:
            # Each result is an array of matching results for a single virtual object
            all_matching_results.extend(result)
    
    # Log summary of results
    log(f"Completed proxy matching with {len(all_matching_results)} total matches across {len(virtual_objects)} virtual objects")
    
    return all_matching_results

# Function to rate a single property of a virtual object against all physical objects
async def rate_single_property(virtual_object, property_name, environment_images, physical_object_database, object_snapshot_map, proxy_matching_results, run_index=1):
    # Log information about the proxy matching results
    log(f"Property rating for {property_name} (run {run_index}) with {len(proxy_matching_results)} proxy matching results")
    
    # If no proxy matching results, try loading directly from file
    if len(proxy_matching_results) == 0:
        try:
            output_dir = os.path.join(script_dir, "output")
            proxy_output_path = os.path.join(output_dir, "proxy_matching_results.json")
            if os.path.exists(proxy_output_path):
                log(f"Loading proxy matching results from {proxy_output_path}")
                with open(proxy_output_path, 'r') as f:
                    proxy_matching_results = json.load(f)
                log(f"Loaded {len(proxy_matching_results)} proxy matching results from file")
                if len(proxy_matching_results) > 0:
                    log(f"Sample: {proxy_matching_results[0].get('utilizationMethod', 'N/A')[:50]}...")
            else:
                log(f"Warning: Proxy matching results file not found at {proxy_output_path}")
        except Exception as e:
            log(f"Error loading proxy matching results: {e}")
    try:
        virtual_object_name = virtual_object.get("objectName", "Unknown Object")
        log(f"Rating {property_name} for virtual object: {virtual_object_name} (run {run_index})")
        
        # Get the property description
        property_description = virtual_object.get(property_name.replace("Value", ""), "")
        
        # Get a property-specific system prompt
        property_system_prompt = get_property_rating_system_prompt(property_name)
        
        # Build the human message content
        human_message_content = []
        
        # 1. Add the virtual object property information
        interaction_deduction = virtual_object.get("interactionDeduction", "No interaction deduction available")
        
        property_text = f"""# Property Rating Task

## Virtual Object: {virtual_object_name}
## Property to Evaluate: {property_name.replace("Value", "")}
## Property Description: {property_description}
## Interaction Deduction: {interaction_deduction}

Please rate how well each physical object can deliver the expected {property_name.replace("Value", "")} haptic feedback described above, considering the deduced interaction pattern when used according to the utilization method.
"""
        human_message_content.append({
            "type": "text", 
            "text": property_text
        })
        
        # 2. Add virtual object snapshot if available
        normalized_object_name = normalize_name(virtual_object_name)
        snapshot_found = False
        
        if virtual_object_name in object_snapshot_map:
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{object_snapshot_map[virtual_object_name]}", 
                    "detail": "high"
                }
            })
            snapshot_found = True
        elif normalized_object_name in object_snapshot_map:
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{object_snapshot_map[normalized_object_name]}", 
                    "detail": "high"
                }
            })
            snapshot_found = True
            
        # 3. Add introduction to physical environment
        human_message_content.append({
            "type": "text", 
            "text": "\n# Physical Environment\nBelow are snapshots of the physical environment with detected objects:"
        })
        
        # 4. Add environment snapshots with their detected objects and utilization methods
        for i, image_base64 in enumerate(environment_images):
            # Add the environment snapshot
            human_message_content.append({
                "type": "text", 
                "text": f"\n## Environment Snapshot {i+1}\n"
            })
            
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{image_base64}", 
                    "detail": "high"
                }
            })
            
            # Add objects from proxy_matching_results for this image
            objects_text = "\n### Objects in this Snapshot\n"
            
            # Group objects by image_id
            image_objects = []
            log(f"Searching through {len(proxy_matching_results)} proxy results for image_id {i}")
            for proxy_result in proxy_matching_results:
                proxy_img_id = proxy_result.get('image_id')
                # Convert to int if it's a string
                if isinstance(proxy_img_id, str):
                    try:
                        proxy_img_id = int(proxy_img_id)
                    except ValueError:
                        pass
                
                # If this object belongs to the current image
                if proxy_img_id == i:
                    log(f"Found object for image_id {i}: {proxy_result.get('physicalObject', 'Unknown')}")
                    has_method = 'utilizationMethod' in proxy_result and proxy_result['utilizationMethod']
                    log(f"Has utilization method: {has_method}")
                    image_objects.append(proxy_result)
            
            # Sort by object_id for consistency
            image_objects.sort(key=lambda x: x.get('object_id', 0))
            
            # Remove duplicates (same object_id)
            unique_objects = []
            seen_object_ids = set()
            for obj in image_objects:
                obj_id = obj.get('object_id')
                if obj_id not in seen_object_ids:
                    seen_object_ids.add(obj_id)
                    unique_objects.append(obj)
            
            if unique_objects:
                for obj in unique_objects:
                    obj_id = obj.get('object_id', 'Unknown')
                    obj_name = obj.get('physicalObject', 'Unknown object')
                    obj_location = obj.get('proxyLocation', 'Unknown position')
                    
                    objects_text += f"- Object ID: {obj_id} - {obj_name} ({obj_location})\n"
                    
                    # Only show utilization methods that match the current virtual object
                    utilization_added = False
                    for proxy_result in proxy_matching_results:
                        # Check if this proxy result matches the current object ID and image ID
                        if (proxy_result.get('object_id') == obj_id and 
                            proxy_result.get('image_id') == i and
                            proxy_result.get('virtualObject') == virtual_object_name):
                            
                            util_method = proxy_result.get("utilizationMethod", "")
                            if util_method:
                                log(f"Found matching utilization method for {virtual_object_name}")
                                objects_text += f"  Utilization Method: {util_method}\n"
                                utilization_added = True
                                break
                    
                    if not utilization_added:
                        objects_text += f"  No utilization method for {virtual_object_name}\n"
                    
                    objects_text += f"  Image ID: {i}\n\n"
            else:
                objects_text += "- No objects found in proxy matching results for this snapshot\n"
            
            human_message_content.append({
                "type": "text", 
                "text": objects_text
            })
        
        # 5. Add final instructions
        human_message_content.append({
            "type": "text", 
            "text": f"""
# Your Task

For each physical object, evaluate the statement: "I felt the haptic feedback closely mimicked the {property_name.replace("Value", "")}" on a 7-point Likert scale:
1 - Strongly Disagree 
2 - Disagree
3 - Somewhat Disagree
4 - Neutral
5 - Somewhat Agree
6 - Agree
7 - Strongly Agree

FORMAT YOUR RESPONSE AS A JSON ARRAY with objects having the following structure:

```json
[
  {{
    "virtualObject": "{virtual_object_name}",
    "property": "{property_name.replace("Value", "")}",
    "physicalObject": "name of the physical object",
    "object_id": 1,
    "image_id": 0,
    "rating": 5,
    "explanation": "Brief explanation of why this rating was given"
  }},
  ...
]
```

IMPORTANT: Include ALL physical objects in your evaluation, even those with low ratings.
"""
        })
        
        # Create the messages
        messages = [
            SystemMessage(content=property_system_prompt),
            HumanMessage(content=human_message_content)
        ]
        
        # Get response from the model
        log(f"Sending property rating request for {property_name} of {virtual_object_name} (run {run_index})")
        response = await property_rating_llm.ainvoke(messages)
        log(f"Received property ratings for {property_name} of {virtual_object_name} (run {run_index})")
        
        # Extract JSON from response
        response_text = response.content
        
        # Try to find JSON array
        json_start = response_text.find("[")
        json_end = response_text.rfind("]") + 1
        if json_start != -1 and json_end > json_start:
            json_content = response_text[json_start:json_end]
        else:
            # Try to find JSON between code blocks
            json_start = response_text.find("```json")
            if json_start != -1:
                json_start += 7  # Length of ```json
                json_end = response_text.find("```", json_start)
                if json_end != -1:
                    json_content = response_text[json_start:json_end].strip()
                else:
                    json_content = response_text[json_start:].strip()
            else:
                # As a fallback, use the entire response
                json_content = response_text
        
        try:
            # Parse the JSON response
            rating_results = json.loads(json_content)
            
            # Add the property value to each result and rename the rating field based on run_index
            rating_key = f"rating_{run_index}"
            for result in rating_results:
                # Get the property value from the virtual object
                property_value = virtual_object.get(property_name, 0.0)
                result["propertyValue"] = property_value
                
                # Rename the rating field
                if "rating" in result:
                    result[rating_key] = result["rating"]
                    del result["rating"]
                
                # Remove any extra fields not in the required output format
                keys_to_keep = ["virtualObject", "property", "physicalObject", "object_id", "image_id", rating_key, "explanation", "propertyValue"]
                for key in list(result.keys()):
                    if key not in keys_to_keep:
                        del result[key]
                
            return rating_results
            
        except json.JSONDecodeError as e:
            log(f"Error parsing property rating JSON for {property_name} of {virtual_object_name} (run {run_index}): {e}")
            log(f"Raw content: {json_content}")
            
            # Return a basic result with the error
            return [{
                "virtualObject": virtual_object_name,
                "property": property_name.replace("Value", ""),
                "error": f"Failed to parse response: {str(e)}",
                "rawResponse": response_text[:500]  # First 500 chars
            }]
            
    except Exception as e:
        log(f"Error in property rating for {property_name} of {virtual_object.get('objectName', 'unknown')} (run {run_index}): {e}")
        import traceback
        log(traceback.format_exc())
        
        # Return a basic result with the error
        return [{
            "virtualObject": virtual_object.get("objectName", "unknown"),
            "property": property_name.replace("Value", ""),
            "error": f"Processing error: {str(e)}"
        }]

# Function to run property ratings for all virtual objects in parallel
async def run_property_ratings(virtual_objects, environment_images, physical_object_database, object_snapshot_map, proxy_matching_results):
    log(f"run_property_ratings received {len(proxy_matching_results)} proxy matching results")
    
    # Check sample proxy result for utilization method
    if len(proxy_matching_results) > 0:
        sample = proxy_matching_results[0]
        log(f"Sample proxy result keys: {list(sample.keys())}")
        if 'utilizationMethod' in sample:
            log(f"Sample utilization method: {sample['utilizationMethod'][:50]}...")
    
    all_tasks = []
    property_names = ["inertiaValue", "interactivityValue", "outlineValue", "textureValue", "hardnessValue", "temperatureValue"]
    
    # Create tasks for each virtual object and its highlighted properties
    for virtual_object in virtual_objects:
        virtual_object_name = virtual_object.get("objectName", "Unknown Object")
        
        # For each property with value > 0, create a rating task
        for property_name in property_names:
            property_value = virtual_object.get(property_name, 0.0)
            
            # Only rate properties that are highlighted (value > 0)
            if property_value > 0:
                log(f"Adding rating tasks for {property_name} of {virtual_object_name} (value: {property_value})")
                
                # Run each property rating 3 times for reliability
                for run_index in range(1, 4):
                    task = rate_single_property(
                        virtual_object,
                        property_name,
                        environment_images,
                        physical_object_database,
                        object_snapshot_map,
                        proxy_matching_results,
                        run_index
                    )
                    all_tasks.append((virtual_object_name, property_name, run_index, task))
    
    # Run all tasks concurrently
    log(f"Running {len(all_tasks)} property rating tasks concurrently (including multiple runs)")
    task_results = await asyncio.gather(*[task[3] for task in all_tasks], return_exceptions=True)
    
    # Process results
    all_rating_results = []
    rating_map = {}  # To track and combine multiple runs
    
    # Process each result and organize by virtual object, property, and physical object
    for i, result in enumerate(task_results):
        if isinstance(result, Exception):
            log(f"Error in property rating task {i}: {result}")
            continue
        else:
            task_info = all_tasks[i]
            virtual_object_name = task_info[0]
            property_name = task_info[1]
            run_index = task_info[2]
            
            # Process each rating result in this batch
            for rating in result:
                if isinstance(rating, dict) and "error" not in rating:
                    # Create a unique key based on object_id and image_id instead of physicalObject name
                    virt_obj = rating.get("virtualObject", "unknown")
                    prop = rating.get("property", "unknown")
                    obj_id = rating.get("object_id", -1)
                    img_id = rating.get("image_id", -1)
                    
                    # Create a unique identifier using only IDs, not names
                    obj_key = f"{virt_obj}:{prop}:{obj_id}:{img_id}"
                    
                    # Get the rating value from this run
                    rating_key = f"rating_{run_index}"
                    rating_value = rating.get(rating_key, 0)
                    
                    if obj_key not in rating_map:
                        # First time seeing this object, create a new entry
                        rating_map[obj_key] = rating.copy()
                    else:
                        # Update existing entry with this run's rating
                        rating_map[obj_key][rating_key] = rating_value
                        
                        # Keep the physical object name from the first occurrence
                        # This prevents inconsistent names from affecting the results
                        if run_index > 1 and "physicalObject" in rating:
                            # We don't update the physicalObject name, keep the original one
                            pass
                else:
                    log(f"Skipping invalid rating result: {rating}")
    
    # Convert the rating map back to a list
    for obj_key, combined_rating in rating_map.items():
        # Make sure all three ratings exist
        for i in range(1, 4):
            rating_key = f"rating_{i}"
            if rating_key not in combined_rating:
                combined_rating[rating_key] = 0  # Use 0 or None for missing ratings
        
        all_rating_results.append(combined_rating)
    
    # Log summary of results
    log(f"Completed property ratings with {len(all_rating_results)} total combined ratings")
    
    return all_rating_results

# Function to rate a single relationship interaction group
async def rate_single_relationship_group(relationship_annotation, contact_object, substrate_objects, environment_images, physical_object_database, object_snapshot_map, enhanced_virtual_objects, proxy_matching_results, substrate_utilization_results, group_index=1):
    try:
        virtual_contact_name = relationship_annotation.get("contactObject", "Unknown Contact Object")
        virtual_substrate_name = relationship_annotation.get("substrateObject", "Unknown Substrate Object")
        annotation_text = relationship_annotation.get("annotationText", "No annotation available")
        
        log(f"Rating relationship group {group_index}: {virtual_contact_name} -> {virtual_substrate_name}")
        
        # Get pre-generated substrate utilization methods for this contact object
        relevant_substrate_methods = []
        for substrate_result in substrate_utilization_results:
            if (substrate_result.get('contactObject_id') == contact_object.get('object_id') and 
                substrate_result.get('contactImage_id') == contact_object.get('image_id') and
                substrate_result.get('virtualContactObject') == virtual_contact_name and
                substrate_result.get('virtualSubstrateObject') == virtual_substrate_name):
                relevant_substrate_methods.append(substrate_result)
        
        log(f"Found {len(relevant_substrate_methods)} pre-generated substrate utilization methods for this contact object")
        
        # Get the contact object's utilization method from proxy matching results
        contact_utilization_method = "No utilization method available"
        for proxy_result in proxy_matching_results:
            if (proxy_result.get('object_id') == contact_object.get('object_id') and 
                proxy_result.get('image_id') == contact_object.get('image_id') and
                proxy_result.get('virtualObject') == virtual_contact_name):
                contact_utilization_method = proxy_result.get("utilizationMethod", "No utilization method available")
                break
        
        # Build the human message content
        human_message_content = []
        
        # 1. Add the relationship information
        # Get virtual contact object details
        virtual_contact_obj = None
        virtual_substrate_obj = None
        for vobj in enhanced_virtual_objects:
            if vobj.get("objectName") == virtual_contact_name:
                virtual_contact_obj = vobj
            elif vobj.get("objectName") == virtual_substrate_name:
                virtual_substrate_obj = vobj
        
        contact_interaction_deduction = virtual_contact_obj.get("interactionDeduction", "No interaction deduction available") if virtual_contact_obj else "No interaction deduction available"
        contact_dimensions = virtual_contact_obj.get("dimensions_meters", {}) if virtual_contact_obj else {}
        substrate_dimensions = virtual_substrate_obj.get("dimensions_meters", {}) if virtual_substrate_obj else {}
        
        # Format dimensions for display
        def format_dimensions(dims):
            if not dims:
                return "No dimensions available"
            return f"Width: {dims.get('x', 'N/A')}m, Height: {dims.get('y', 'N/A')}m, Depth: {dims.get('z', 'N/A')}m"
        
        relationship_text = f"""# Relationship Rating Task (Group {group_index})

## Virtual Object Relationship
- **Contact Object**: {virtual_contact_name}
- **Substrate Object**: {virtual_substrate_name}
- **Expected Haptic Feedback**: {annotation_text}

## Virtual Contact Object Details
- **Interaction Deduction**: {contact_interaction_deduction}
- **Dimensions**: {format_dimensions(contact_dimensions)}

## Virtual Substrate Object Details
- **Dimensions**: {format_dimensions(substrate_dimensions)}

## Physical Object Assignment
- **Contact Object**: {contact_object.get('object', 'Unknown')} (ID: {contact_object.get('object_id')}, Image: {contact_object.get('image_id')})
- **Utilization Method**: {contact_utilization_method}
- **Substrate Objects**: All other physical objects listed below

Please rate how well each pair (contact + substrate) can simulate the expected haptic feedback described above.
"""
        human_message_content.append({
            "type": "text", 
            "text": relationship_text
        })
        
        # 2. Add virtual object snapshots if available
        # Add virtual contact object snapshot
        contact_snapshot_found = False
        normalized_contact_name = normalize_name(virtual_contact_name)
        
        # Try direct match first
        if virtual_contact_name in object_snapshot_map:
            log(f"Found snapshot for contact object: {virtual_contact_name} (direct match)")
            human_message_content.append({
                "type": "text",
                "text": f"\n## Virtual Contact Object: {virtual_contact_name}\n"
            })
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{object_snapshot_map[virtual_contact_name]}", 
                    "detail": "high"
                }
            })
            contact_snapshot_found = True
        # Then try normalized match
        elif normalized_contact_name in object_snapshot_map:
            log(f"Found snapshot for contact object: {virtual_contact_name} (normalized as {normalized_contact_name})")
            human_message_content.append({
                "type": "text",
                "text": f"\n## Virtual Contact Object: {virtual_contact_name}\n"
            })
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{object_snapshot_map[normalized_contact_name]}", 
                    "detail": "high"
                }
            })
            contact_snapshot_found = True
        # Finally try fuzzy match
        else:
            # Try to find partial matches
            potential_matches = [name for name in object_snapshot_map.keys() 
                               if normalized_contact_name in normalize_name(name) or normalize_name(name) in normalized_contact_name]
            
            if potential_matches:
                best_match = potential_matches[0]  # Take the first match
                log(f"Found snapshot for contact object: {virtual_contact_name} (fuzzy match: {best_match})")
                human_message_content.append({
                    "type": "text",
                    "text": f"\n## Virtual Contact Object: {virtual_contact_name}\n"
                })
                human_message_content.append({
                    "type": "image_url", 
                    "image_url": {
                        "url": f"data:image/jpeg;base64,{object_snapshot_map[best_match]}", 
                        "detail": "high"
                    }
                })
                contact_snapshot_found = True
        
        if not contact_snapshot_found:
            log(f"No snapshot found for contact object: {virtual_contact_name} (normalized: {normalized_contact_name})")
        
        # Add virtual substrate object snapshot
        substrate_snapshot_found = False
        normalized_substrate_name = normalize_name(virtual_substrate_name)
        
        # Try direct match first
        if virtual_substrate_name in object_snapshot_map:
            log(f"Found snapshot for substrate object: {virtual_substrate_name} (direct match)")
            human_message_content.append({
                "type": "text",
                "text": f"\n## Virtual Substrate Object: {virtual_substrate_name}\n"
            })
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{object_snapshot_map[virtual_substrate_name]}", 
                    "detail": "high"
                }
            })
            substrate_snapshot_found = True
        # Then try normalized match
        elif normalized_substrate_name in object_snapshot_map:
            log(f"Found snapshot for substrate object: {virtual_substrate_name} (normalized as {normalized_substrate_name})")
            human_message_content.append({
                "type": "text",
                "text": f"\n## Virtual Substrate Object: {virtual_substrate_name}\n"
            })
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{object_snapshot_map[normalized_substrate_name]}", 
                    "detail": "high"
                }
            })
            substrate_snapshot_found = True
        # Finally try fuzzy match
        else:
            # Try to find partial matches
            potential_matches = [name for name in object_snapshot_map.keys() 
                               if normalized_substrate_name in normalize_name(name) or normalize_name(name) in normalized_substrate_name]
            
            if potential_matches:
                best_match = potential_matches[0]  # Take the first match
                log(f"Found snapshot for substrate object: {virtual_substrate_name} (fuzzy match: {best_match})")
                human_message_content.append({
                    "type": "text",
                    "text": f"\n## Virtual Substrate Object: {virtual_substrate_name}\n"
                })
                human_message_content.append({
                    "type": "image_url", 
                    "image_url": {
                        "url": f"data:image/jpeg;base64,{object_snapshot_map[best_match]}", 
                        "detail": "high"
                    }
                })
                substrate_snapshot_found = True
        
        if not substrate_snapshot_found:
            log(f"No snapshot found for substrate object: {virtual_substrate_name} (normalized: {normalized_substrate_name})")
        
        # 3. Add environment snapshots with their detected objects
        for i, image_base64 in enumerate(environment_images):
            # Add the environment snapshot
            human_message_content.append({
                "type": "text", 
                "text": f"\n## Environment Snapshot {i+1}\n"
            })
            
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{image_base64}", 
                    "detail": "high"
                }
            })
            
            # Add the detected objects for this snapshot
            objects_in_snapshot = physical_object_database.get(str(i), [])
            objects_text = "\n### Physical Objects in this Snapshot\n"
            
            if objects_in_snapshot:
                for obj in objects_in_snapshot:
                    # Highlight the contact object
                    if (obj['object_id'] == contact_object.get('object_id') and 
                        obj.get('image_id') == contact_object.get('image_id')):
                        objects_text += f"- **CONTACT OBJECT** - Object ID {obj['object_id']}: {obj['object']} ({obj['position']})\n"
                    else:
                        objects_text += f"- Object ID {obj['object_id']}: {obj['object']} ({obj['position']}) - *Substrate candidate*\n"
                    objects_text += f"  Image ID: {i}\n"
            else:
                # Check if we should look for objects in a different format
                objects_in_snapshot = physical_object_database.get(i, [])
                if objects_in_snapshot:
                    for obj in objects_in_snapshot:
                        if (obj['object_id'] == contact_object.get('object_id') and 
                            obj.get('image_id') == contact_object.get('image_id')):
                            objects_text += f"- **CONTACT OBJECT** - Object ID {obj['object_id']}: {obj['object']} ({obj['position']})\n"
                        else:
                            objects_text += f"- Object ID {obj['object_id']}: {obj['object']} ({obj['position']}) - *Substrate candidate*\n"
                        objects_text += f"  Image ID: {i}\n"
                else:
                    objects_text += "- No objects detected in this snapshot\n"
                
            human_message_content.append({
                "type": "text", 
                "text": objects_text
            })
        
        # 4. Add pre-generated substrate utilization methods
        if relevant_substrate_methods:
            substrate_methods_text = "\n# Pre-generated Substrate Utilization Methods\n"
            substrate_methods_text += f"For contact object: {contact_object.get('object')} (using method: {contact_utilization_method})\n\n"
            
            for method in relevant_substrate_methods:
                substrate_obj_name = method.get('physicalSubstrateObject', 'Unknown')
                substrate_method = method.get('substrateUtilizationMethod', 'No method available')
                substrate_methods_text += f"**{substrate_obj_name}**: {substrate_method}\n\n"
            
            human_message_content.append({
                "type": "text", 
                "text": substrate_methods_text
            })
        
        # 5. Add final instructions
        human_message_content.append({
            "type": "text", 
            "text": f"""
# Your Task

You have been provided with pre-generated substrate utilization methods above. Your task is to evaluate how well each contact-substrate pair can deliver the expected haptic feedback.

**Contact Object**: {contact_object.get('object')} (using utilization method: {contact_utilization_method})

For each substrate object and its corresponding utilization method, rate the pair on a 7-point Likert scale for:
1. **Harmony**: "I felt the haptic feedback was well coordinated with visual feedback"
2. **Expressivity**: "I felt the contact object effectively conveyed substrate properties and interaction variations through my hand"  
3. **Realism**: "I felt using this physical contact object on this physical substrate closely simulated the intended haptic feedback"

**Critical Evaluation Guidelines:**
- Consider both the contact object's utilization method AND the provided substrate utilization method
- Rate based on how well the combined utilization methods would deliver the expected haptic feedback: "{annotation_text}"
- Be critical in your assessment using the provided rubrics

FORMAT YOUR RESPONSE AS A JSON ARRAY as specified in the system prompt, including both contactUtilizationMethod and the provided substrateUtilizationMethod for each pair.
"""
        })
        
        # Create the messages
        messages = [
            SystemMessage(content=relationship_rating_system_prompt),
            HumanMessage(content=human_message_content)
        ]
        
        # Get response from the model
        log(f"Sending relationship rating request for group {group_index}")
        log(f"LangSmith tracing status: LANGCHAIN_TRACING_V2={os.environ.get('LANGCHAIN_TRACING_V2', 'Not set')}")
        log(f"LangSmith API key status: {'Set' if os.environ.get('LANGCHAIN_API_KEY') else 'Not set'}")
        
        try:
            response = await relationship_rating_llm.ainvoke(messages)
            log(f"Successfully received relationship ratings for group {group_index}")
        except Exception as e:
            log(f"Error during relationship rating LLM call for group {group_index}: {e}")
            raise
        
        # Extract JSON from response
        response_text = response.content
        
        # Try to find JSON array
        json_start = response_text.find("[")
        json_end = response_text.rfind("]") + 1
        if json_start != -1 and json_end > json_start:
            json_content = response_text[json_start:json_end]
        else:
            # Try to find JSON between code blocks
            json_start = response_text.find("```json")
            if json_start != -1:
                json_start += 7  # Length of ```json
                json_end = response_text.find("```", json_start)
                if json_end != -1:
                    json_content = response_text[json_start:json_end].strip()
                else:
                    json_content = response_text[json_start:].strip()
            else:
                # As a fallback, use the entire response
                json_content = response_text
        
        try:
            # Parse the JSON response
            rating_results = json.loads(json_content)
            
            # Add group information to each result
            for result in rating_results:
                result["group_index"] = group_index
                result["virtualContactObject"] = virtual_contact_name
                result["virtualSubstrateObject"] = virtual_substrate_name
                result["expectedHapticFeedback"] = annotation_text
                
            return rating_results
            
        except json.JSONDecodeError as e:
            log(f"Error parsing relationship rating JSON for group {group_index}: {e}")
            log(f"Raw content: {json_content}")
            
            # Return a basic result with the error
            return [{
                "group_index": group_index,
                "virtualContactObject": virtual_contact_name,
                "virtualSubstrateObject": virtual_substrate_name,
                "error": f"Failed to parse response: {str(e)}",
                "rawResponse": response_text[:500]  # First 500 chars
            }]
            
    except Exception as e:
        log(f"Error in relationship rating for group {group_index}: {e}")
        import traceback
        log(traceback.format_exc())
        
        # Return a basic result with the error
        return [{
            "group_index": group_index,
            "error": f"Processing error: {str(e)}"
        }]

# Function to run relationship ratings for all relationships in parallel
async def run_relationship_ratings(haptic_annotation_json, environment_images, physical_object_database, object_snapshot_map, enhanced_virtual_objects, proxy_matching_results, substrate_utilization_results):
    if not haptic_annotation_json:
        log("No haptic annotation data provided for relationship ratings")
        return []
    
    try:
        # Parse the haptic annotation JSON
        haptic_data = json.loads(haptic_annotation_json)
        relationship_annotations = haptic_data.get("relationshipAnnotations", [])
        
        if not relationship_annotations:
            log("No relationship annotations found in haptic data")
            return []
        
        log(f"Found {len(relationship_annotations)} relationship annotations")
        
        # Get all physical objects from the database
        all_physical_objects = []
        for image_id, objects in physical_object_database.items():
            for obj in objects:
                all_physical_objects.append(obj)
        
        log(f"Found {len(all_physical_objects)} total physical objects for relationship rating")
        
        # Create rating tasks - for each relationship, create multiple groups
        # Each group assigns one physical object as contact and rates it against all others as substrate
        all_tasks = []
        group_counter = 1
        
        for relationship in relationship_annotations:
            # Create one group for each physical object as the contact object
            for contact_obj in all_physical_objects:
                # Get all other objects as substrate candidates
                substrate_objects = [obj for obj in all_physical_objects 
                                   if not (obj['object_id'] == contact_obj['object_id'] and 
                                          obj.get('image_id') == contact_obj.get('image_id'))]
                
                if len(substrate_objects) > 0:
                    task = rate_single_relationship_group(
                        relationship,
                        contact_obj,
                        substrate_objects,
                        environment_images,
                        physical_object_database,
                        object_snapshot_map,
                        enhanced_virtual_objects,
                        proxy_matching_results,
                        substrate_utilization_results,
                        group_counter
                    )
                    all_tasks.append(task)
                    group_counter += 1
        
        # Run all tasks concurrently
        log(f"Running {len(all_tasks)} relationship rating tasks concurrently")
        task_results = await asyncio.gather(*all_tasks, return_exceptions=True)
        
        # Process results
        all_relationship_results = []
        for i, result in enumerate(task_results):
            if isinstance(result, Exception):
                log(f"Error in relationship rating task {i}: {result}")
                continue
            else:
                # Each result is an array of rating results for a single group
                all_relationship_results.extend(result)
        
        # Log summary of results
        log(f"Completed relationship ratings with {len(all_relationship_results)} total ratings")
        
        return all_relationship_results
        
    except Exception as e:
        log(f"Error processing relationship ratings: {e}")
        import traceback
        log(traceback.format_exc())
        return []

# Modify the run_concurrent_tasks function to include proxy matching
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
    
    # Run initial tasks concurrently and get results
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
    
    # Create object snapshot map for virtual objects
    object_snapshot_map = {}
    for snapshot in virtual_object_snapshots:
        if 'objectName' in snapshot and 'imageBase64' in snapshot:
            original_name = snapshot['objectName']
            normalized_name = normalize_name(original_name)
            object_snapshot_map[normalized_name] = snapshot['imageBase64']
            object_snapshot_map[original_name] = snapshot['imageBase64']
    
    # Get the actual data from the results
    physical_object_database = results.get("physical_result", {})
    enhanced_virtual_objects = results.get("virtual_result", [])
    
    # Run proxy matching if both physical and virtual objects are available
    if environment_image_base64_list and haptic_annotation_json:
        log("Setting up proxy matching task")
        
        # Run proxy matching
        proxy_matching_results = await run_proxy_matching(
            enhanced_virtual_objects, 
            environment_image_base64_list, 
            physical_object_database,
            object_snapshot_map
        )
        
        # Add to results
        results["proxy_matching_result"] = proxy_matching_results
        
        # Log sample proxy matching results for debugging
        if len(proxy_matching_results) > 0:
            log("Sample proxy matching result:")
            log(f"- virtualObject: {proxy_matching_results[0].get('virtualObject', 'N/A')}")
            log(f"- physicalObject: {proxy_matching_results[0].get('physicalObject', 'N/A')}")
            log(f"- object_id: {proxy_matching_results[0].get('object_id', 'N/A')}")
            log(f"- image_id: {proxy_matching_results[0].get('image_id', 'N/A')}")
            log(f"- utilizationMethod: {proxy_matching_results[0].get('utilizationMethod', 'N/A')}")
        else:
            log("Warning: No proxy matching results available!")
        
        # Run property rating and substrate utilization method generation concurrently
        log("Setting up concurrent property rating and substrate utilization method generation tasks")
        
        # Create the concurrent tasks
        property_rating_task = run_property_ratings(
            enhanced_virtual_objects,
            environment_image_base64_list,
            physical_object_database,
            object_snapshot_map,
            proxy_matching_results
        )
        
        substrate_utilization_task = run_substrate_utilization_methods(
            haptic_annotation_json,
            environment_image_base64_list,
            physical_object_database,
            object_snapshot_map,
            enhanced_virtual_objects,
            proxy_matching_results
        )
        
        # Run both tasks concurrently
        log("Running property rating and substrate utilization method generation concurrently")
        property_rating_results, substrate_utilization_results = await asyncio.gather(
            property_rating_task,
            substrate_utilization_task
        )
        
        # Add to results
        results["substrate_utilization_result"] = substrate_utilization_results
        
        # Log sample substrate utilization results for debugging
        if len(substrate_utilization_results) > 0:
            log("Sample substrate utilization result:")
            log(f"- virtualContactObject: {substrate_utilization_results[0].get('virtualContactObject', 'N/A')}")
            log(f"- virtualSubstrateObject: {substrate_utilization_results[0].get('virtualSubstrateObject', 'N/A')}")
            log(f"- physicalContactObject: {substrate_utilization_results[0].get('physicalContactObject', 'N/A')}")
            log(f"- physicalSubstrateObject: {substrate_utilization_results[0].get('physicalSubstrateObject', 'N/A')}")
            log(f"- substrateUtilizationMethod: {substrate_utilization_results[0].get('substrateUtilizationMethod', 'N/A')[:50]}...")
        else:
            log("Warning: No substrate utilization results available!")
        
        # Run relationship rating after substrate utilization is complete
        log("Setting up relationship rating task")
        log(f"Available data for relationship rating:")
        log(f"- haptic_annotation_json: {'Available' if haptic_annotation_json else 'None'}")
        log(f"- environment_images: {len(environment_image_base64_list) if environment_image_base64_list else 0}")
        log(f"- proxy_matching_results: {len(proxy_matching_results) if proxy_matching_results else 0}")
        log(f"- substrate_utilization_results: {len(substrate_utilization_results) if substrate_utilization_results else 0}")
        
        relationship_rating_results = await run_relationship_ratings(
            haptic_annotation_json,
            environment_image_base64_list,
            physical_object_database,
            object_snapshot_map,
            enhanced_virtual_objects,
            proxy_matching_results,
            substrate_utilization_results
        )
        
        log(f"Relationship rating completed with {len(relationship_rating_results) if relationship_rating_results else 0} results")
        
        # Add to results
        results["property_rating_result"] = property_rating_results
        results["relationship_rating_result"] = relationship_rating_results
    
    return results

# Add a utility function to handle key renaming in JSON structures
def rename_key_in_json(data, old_key, new_key):
    """Recursively rename a key in a JSON-like data structure"""
    if isinstance(data, dict):
        # Create a new dict with updated keys
        new_dict = {}
        for k, v in data.items():
            # Replace the key if it matches
            new_k = new_key if k == old_key else k
            
            # Process the value (which might contain further dict/list structures)
            new_dict[new_k] = rename_key_in_json(v, old_key, new_key)
        return new_dict
    elif isinstance(data, list):
        # Process each item in the list
        return [rename_key_in_json(item, old_key, new_key) for item in data]
    else:
        # Return primitives unchanged
        return data

# Function to calculate final scores and update proxy matching results
def calculate_final_scores(property_rating_results, proxy_matching_results):
    """Calculate final weighted scores for each virtual-physical pair and update proxy matching results"""
    log("Calculating final weighted scores for each virtual-physical pair")
    
    # Create a dictionary to store scores for each virtual-physical pair
    scores = {}
    
    # Process each property rating result
    for rating in property_rating_results:
        if "error" in rating:
            continue
            
        # Create a key for this virtual-physical pair
        virt_obj = rating.get("virtualObject", "unknown")
        obj_id = rating.get("object_id", -1)
        img_id = rating.get("image_id", -1)
        
        # Use object_id and image_id as the primary identifiers
        pair_key = f"{virt_obj}:{obj_id}:{img_id}"
        property_name = rating.get("property", "unknown")
        
        # Calculate the mean of the three ratings
        rating_1 = rating.get("rating_1", 0)
        rating_2 = rating.get("rating_2", 0)
        rating_3 = rating.get("rating_3", 0)
        mean_rating = (rating_1 + rating_2 + rating_3) / 3 if (rating_1 or rating_2 or rating_3) else 0
        
        # Get the property value (significance)
        property_value = rating.get("propertyValue", 0.0)
        
        # Calculate the weighted score for this property
        weighted_score = mean_rating * property_value
        
        # Initialize the entry if it doesn't exist
        if pair_key not in scores:
            scores[pair_key] = {
                "virtual_object": virt_obj,
                "object_id": obj_id,
                "image_id": img_id,
                "total_score": 0,
                "property_scores": {}
            }
        
        # Add the weighted score to the total
        scores[pair_key]["total_score"] += weighted_score
        
        # Store the individual property score
        scores[pair_key]["property_scores"][property_name] = {
            "mean_rating": mean_rating,
            "property_value": property_value,
            "weighted_score": weighted_score
        }
    
    # Update proxy matching results with the calculated scores
    for proxy_result in proxy_matching_results:
        virt_obj = proxy_result.get("virtualObject", "unknown")
        obj_id = proxy_result.get("object_id", -1)
        img_id = proxy_result.get("image_id", -1)
        
        pair_key = f"{virt_obj}:{obj_id}:{img_id}"
        
        if pair_key in scores:
            # Add the total score to the proxy result
            proxy_result["rating_score"] = scores[pair_key]["total_score"]
            
            # Add detailed property scores if desired
            proxy_result["property_scores"] = scores[pair_key]["property_scores"]
    
    return proxy_matching_results

# Function to generate substrate utilization methods for a single contact-substrate relationship
async def generate_substrate_utilization_for_contact(relationship_annotation, contact_object, environment_images, physical_object_database, object_snapshot_map, enhanced_virtual_objects, proxy_matching_results):
    try:
        virtual_contact_name = relationship_annotation.get("contactObject", "Unknown Contact Object")
        virtual_substrate_name = relationship_annotation.get("substrateObject", "Unknown Substrate Object")
        annotation_text = relationship_annotation.get("annotationText", "No annotation available")
        
        log(f"Generating substrate utilization methods for {virtual_contact_name} -> {virtual_substrate_name}")
        
        # Get the contact object's utilization method
        contact_utilization_method = "No utilization method available"
        for proxy_result in proxy_matching_results:
            if (proxy_result.get('object_id') == contact_object.get('object_id') and 
                proxy_result.get('image_id') == contact_object.get('image_id') and
                proxy_result.get('virtualObject') == virtual_contact_name):
                contact_utilization_method = proxy_result.get("utilizationMethod", "No utilization method available")
                break
        
        # Build the human message content
        human_message_content = []
        
        # 1. Add the relationship information
        # Get virtual contact and substrate object details
        virtual_contact_obj = None
        virtual_substrate_obj = None
        for vobj in enhanced_virtual_objects:
            if vobj.get("objectName") == virtual_contact_name:
                virtual_contact_obj = vobj
            elif vobj.get("objectName") == virtual_substrate_name:
                virtual_substrate_obj = vobj
        
        contact_interaction_deduction = virtual_contact_obj.get("interactionDeduction", "No interaction deduction available") if virtual_contact_obj else "No interaction deduction available"
        contact_dimensions = virtual_contact_obj.get("dimensions_meters", {}) if virtual_contact_obj else {}
        substrate_dimensions = virtual_substrate_obj.get("dimensions_meters", {}) if virtual_substrate_obj else {}
        
        # Format dimensions for display
        def format_dimensions(dims):
            if not dims:
                return "No dimensions available"
            return f"Width: {dims.get('x', 'N/A')}m, Height: {dims.get('y', 'N/A')}m, Depth: {dims.get('z', 'N/A')}m"
        
        relationship_text = f"""# Substrate Utilization Method Generation

## Virtual Object Relationship
- **Contact Object**: {virtual_contact_name}
- **Substrate Object**: {virtual_substrate_name}
- **Expected Haptic Feedback**: {annotation_text}

## Virtual Contact Object Details
- **Interaction Deduction**: {contact_interaction_deduction}
- **Dimensions**: {format_dimensions(contact_dimensions)}

## Virtual Substrate Object Details
- **Dimensions**: {format_dimensions(substrate_dimensions)}

## Physical Contact Object Assignment
- **Contact Object**: {contact_object.get('object', 'Unknown')} (ID: {contact_object.get('object_id')}, Image: {contact_object.get('image_id')})
- **Contact Utilization Method**: {contact_utilization_method}

Your task is to determine how each physical object in the environment can be utilized as a substrate to work with this specific physical contact object and its utilization method.
"""
        human_message_content.append({
            "type": "text", 
            "text": relationship_text
        })
        
        # 2. Add virtual object snapshots if available
        # Add virtual contact object snapshot
        contact_snapshot_found = False
        normalized_contact_name = normalize_name(virtual_contact_name)
        
        # Try direct match first
        if virtual_contact_name in object_snapshot_map:
            log(f"Found snapshot for contact object: {virtual_contact_name} (direct match)")
            human_message_content.append({
                "type": "text",
                "text": f"\n## Virtual Contact Object: {virtual_contact_name}\n"
            })
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{object_snapshot_map[virtual_contact_name]}", 
                    "detail": "high"
                }
            })
            contact_snapshot_found = True
        # Then try normalized match
        elif normalized_contact_name in object_snapshot_map:
            log(f"Found snapshot for contact object: {virtual_contact_name} (normalized as {normalized_contact_name})")
            human_message_content.append({
                "type": "text",
                "text": f"\n## Virtual Contact Object: {virtual_contact_name}\n"
            })
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{object_snapshot_map[normalized_contact_name]}", 
                    "detail": "high"
                }
            })
            contact_snapshot_found = True
        # Finally try fuzzy match
        else:
            # Try to find partial matches
            potential_matches = [name for name in object_snapshot_map.keys() 
                               if normalized_contact_name in normalize_name(name) or normalize_name(name) in normalized_contact_name]
            
            if potential_matches:
                best_match = potential_matches[0]  # Take the first match
                log(f"Found snapshot for contact object: {virtual_contact_name} (fuzzy match: {best_match})")
                human_message_content.append({
                    "type": "text",
                    "text": f"\n## Virtual Contact Object: {virtual_contact_name}\n"
                })
                human_message_content.append({
                    "type": "image_url", 
                    "image_url": {
                        "url": f"data:image/jpeg;base64,{object_snapshot_map[best_match]}", 
                        "detail": "high"
                    }
                })
                contact_snapshot_found = True
        
        if not contact_snapshot_found:
            log(f"No snapshot found for contact object: {virtual_contact_name} (normalized: {normalized_contact_name})")
        
        # Add virtual substrate object snapshot
        substrate_snapshot_found = False
        normalized_substrate_name = normalize_name(virtual_substrate_name)
        
        # Try direct match first
        if virtual_substrate_name in object_snapshot_map:
            log(f"Found snapshot for substrate object: {virtual_substrate_name} (direct match)")
            human_message_content.append({
                "type": "text",
                "text": f"\n## Virtual Substrate Object: {virtual_substrate_name}\n"
            })
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{object_snapshot_map[virtual_substrate_name]}", 
                    "detail": "high"
                }
            })
            substrate_snapshot_found = True
        # Then try normalized match
        elif normalized_substrate_name in object_snapshot_map:
            log(f"Found snapshot for substrate object: {virtual_substrate_name} (normalized as {normalized_substrate_name})")
            human_message_content.append({
                "type": "text",
                "text": f"\n## Virtual Substrate Object: {virtual_substrate_name}\n"
            })
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{object_snapshot_map[normalized_substrate_name]}", 
                    "detail": "high"
                }
            })
            substrate_snapshot_found = True
        # Finally try fuzzy match
        else:
            # Try to find partial matches
            potential_matches = [name for name in object_snapshot_map.keys() 
                               if normalized_substrate_name in normalize_name(name) or normalize_name(name) in normalized_substrate_name]
            
            if potential_matches:
                best_match = potential_matches[0]  # Take the first match
                log(f"Found snapshot for substrate object: {virtual_substrate_name} (fuzzy match: {best_match})")
                human_message_content.append({
                    "type": "text",
                    "text": f"\n## Virtual Substrate Object: {virtual_substrate_name}\n"
                })
                human_message_content.append({
                    "type": "image_url", 
                    "image_url": {
                        "url": f"data:image/jpeg;base64,{object_snapshot_map[best_match]}", 
                        "detail": "high"
                    }
                })
                substrate_snapshot_found = True
        
        if not substrate_snapshot_found:
            log(f"No snapshot found for substrate object: {virtual_substrate_name} (normalized: {normalized_substrate_name})")
        
        # 3. Add environment snapshots with their detected objects
        for i, image_base64 in enumerate(environment_images):
            # Add the environment snapshot
            human_message_content.append({
                "type": "text", 
                "text": f"\n## Environment Snapshot {i+1}\n"
            })
            
            human_message_content.append({
                "type": "image_url", 
                "image_url": {
                    "url": f"data:image/jpeg;base64,{image_base64}", 
                    "detail": "high"
                }
            })
            
            # Add the detected objects for this snapshot
            objects_in_snapshot = physical_object_database.get(str(i), [])
            objects_text = "\n### Physical Objects in this Snapshot\n"
            
            if objects_in_snapshot:
                for obj in objects_in_snapshot:
                    # Highlight the contact object
                    if (obj['object_id'] == contact_object.get('object_id') and 
                        obj.get('image_id') == contact_object.get('image_id')):
                        objects_text += f"- **CONTACT OBJECT** - Object ID {obj['object_id']}: {obj['object']} ({obj['position']})\n"
                    else:
                        objects_text += f"- Object ID {obj['object_id']}: {obj['object']} ({obj['position']}) - *Substrate candidate*\n"
                    objects_text += f"  Image ID: {i}\n"
            else:
                # Check if we should look for objects in a different format
                objects_in_snapshot = physical_object_database.get(i, [])
                if objects_in_snapshot:
                    for obj in objects_in_snapshot:
                        if (obj['object_id'] == contact_object.get('object_id') and 
                            obj.get('image_id') == contact_object.get('image_id')):
                            objects_text += f"- **CONTACT OBJECT** - Object ID {obj['object_id']}: {obj['object']} ({obj['position']})\n"
                        else:
                            objects_text += f"- Object ID {obj['object_id']}: {obj['object']} ({obj['position']}) - *Substrate candidate*\n"
                        objects_text += f"  Image ID: {i}\n"
                else:
                    objects_text += "- No objects detected in this snapshot\n"
                
            human_message_content.append({
                "type": "text", 
                "text": objects_text
            })
        
        # 4. Add final instructions
        human_message_content.append({
            "type": "text", 
            "text": f"""
# Your Task

For each physical object in the environment (except the contact object itself), determine how it could be utilized as a substrate to work with the physical contact object "{contact_object.get('object')}" using its utilization method: "{contact_utilization_method}"

Consider:
- The expected haptic feedback: "{annotation_text}"
- How each substrate candidate should be positioned, oriented, or prepared
- What specific properties or features of each substrate should be utilized
- How each substrate would interact with the contact object's utilization method
- What modifications or setup might be needed for each substrate

Focus on creating substrate utilization methods that would enable the delivery of the expected haptic feedback: "{annotation_text}"

FORMAT YOUR RESPONSE as specified in the system prompt, including detailed substrate utilization methods for ALL physical objects as potential substrates.
"""
        })
        
        # Create the messages
        messages = [
            SystemMessage(content=substrate_utilization_system_prompt),
            HumanMessage(content=human_message_content)
        ]
        
        # Get response from the model
        log(f"Sending substrate utilization request for {virtual_contact_name} -> {virtual_substrate_name}")
        response = await substrate_utilization_llm.ainvoke(messages)
        log(f"Received substrate utilization methods for {virtual_contact_name} -> {virtual_substrate_name}")
        
        # Extract JSON from response
        response_text = response.content
        
        # Try to find JSON array
        json_start = response_text.find("[")
        json_end = response_text.rfind("]") + 1
        if json_start != -1 and json_end > json_start:
            json_content = response_text[json_start:json_end]
        else:
            # Try to find JSON between code blocks
            json_start = response_text.find("```json")
            if json_start != -1:
                json_start += 7  # Length of ```json
                json_end = response_text.find("```", json_start)
                if json_end != -1:
                    json_content = response_text[json_start:json_end].strip()
                else:
                    json_content = response_text[json_start:].strip()
            else:
                # As a fallback, use the entire response
                json_content = response_text
        
        try:
            # Parse the JSON response
            utilization_results = json.loads(json_content)
            
            # Add contact object information to each result
            for result in utilization_results:
                result["relationshipAnnotation"] = relationship_annotation
                
            return utilization_results
            
        except json.JSONDecodeError as e:
            log(f"Error parsing substrate utilization JSON for {virtual_contact_name} -> {virtual_substrate_name}: {e}")
            log(f"Raw content: {json_content}")
            
            # Return a basic result with the error
            return [{
                "virtualContactObject": virtual_contact_name,
                "virtualSubstrateObject": virtual_substrate_name,
                "physicalContactObject": contact_object.get('object', 'Unknown'),
                "error": f"Failed to parse response: {str(e)}",
                "rawResponse": response_text[:500]  # First 500 chars
            }]
            
    except Exception as e:
        log(f"Error in substrate utilization generation for {relationship_annotation.get('contactObject', 'unknown')} -> {relationship_annotation.get('substrateObject', 'unknown')}: {e}")
        import traceback
        log(traceback.format_exc())
        
        # Return a basic result with the error
        return [{
            "virtualContactObject": relationship_annotation.get("contactObject", "unknown"),
            "virtualSubstrateObject": relationship_annotation.get("substrateObject", "unknown"),
            "error": f"Processing error: {str(e)}"
        }]

# Function to run substrate utilization methods generation for all relationships
async def run_substrate_utilization_methods(haptic_annotation_json, environment_images, physical_object_database, object_snapshot_map, enhanced_virtual_objects, proxy_matching_results):
    if not haptic_annotation_json:
        log("No haptic annotation data provided for substrate utilization methods")
        return []
    
    try:
        # Parse the haptic annotation JSON
        haptic_data = json.loads(haptic_annotation_json)
        relationship_annotations = haptic_data.get("relationshipAnnotations", [])
        
        if not relationship_annotations:
            log("No relationship annotations found in haptic data")
            return []
        
        log(f"Found {len(relationship_annotations)} relationship annotations")
        
        # Get all physical objects from the database
        all_physical_objects = []
        for image_id, objects in physical_object_database.items():
            for obj in objects:
                all_physical_objects.append(obj)
        
        log(f"Found {len(all_physical_objects)} total physical objects for substrate utilization method generation")
        
        # Create substrate utilization tasks - for each relationship, create one task per physical object as contact
        all_tasks = []
        
        for relationship in relationship_annotations:
            virtual_contact_name = relationship.get("contactObject", "")
            virtual_substrate_name = relationship.get("substrateObject", "")
            
            # For each physical object that could be a contact object (has a utilization method for this virtual contact)
            for contact_obj in all_physical_objects:
                # Check if this physical object has a utilization method for the virtual contact object
                has_contact_utilization = False
                for proxy_result in proxy_matching_results:
                    if (proxy_result.get('object_id') == contact_obj.get('object_id') and 
                        proxy_result.get('image_id') == contact_obj.get('image_id') and
                        proxy_result.get('virtualObject') == virtual_contact_name):
                        has_contact_utilization = True
                        break
                
                # Only create a task if this physical object can serve as the contact object
                if has_contact_utilization:
                    log(f"Creating substrate utilization task for {virtual_contact_name} -> {virtual_substrate_name} with contact object {contact_obj.get('object')} (ID: {contact_obj.get('object_id')})")
                    
                    task = generate_substrate_utilization_for_contact(
                        relationship,
                        contact_obj,
                        environment_images,
                        physical_object_database,
                        object_snapshot_map,
                        enhanced_virtual_objects,
                        proxy_matching_results
                    )
                    all_tasks.append(task)
        
        # Run all tasks concurrently
        log(f"Running {len(all_tasks)} substrate utilization tasks concurrently")
        task_results = await asyncio.gather(*all_tasks, return_exceptions=True)
        
        # Process results
        all_substrate_utilization_results = []
        for i, result in enumerate(task_results):
            if isinstance(result, Exception):
                log(f"Error in substrate utilization task {i}: {result}")
                continue
            else:
                # Each result is an array of utilization results for a single contact-substrate relationship
                all_substrate_utilization_results.extend(result)
        
        # Log summary of results
        log(f"Completed substrate utilization method generation with {len(all_substrate_utilization_results)} total utilization methods")
        
        return all_substrate_utilization_results
        
    except Exception as e:
        log(f"Error processing substrate utilization methods: {e}")
        import traceback
        log(traceback.format_exc())
        return []

try:
    # Create a variable to store the processing results
    result = {"status": "success", "message": "Processing complete"}
    
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
    
    # Process proxy matching results if available
    if environment_image_base64_list and haptic_annotation_json:
        log("Processing completed proxy matching results")
        proxy_matching_results = concurrent_results.get("proxy_matching_result", [])
        
        # Save proxy matching results
        output_dir = os.path.join(script_dir, "output")
        proxy_output_path = os.path.join(output_dir, "proxy_matching_results.json")
        
        # Ensure output directory exists
        os.makedirs(output_dir, exist_ok=True)
        
        # Convert any imageId keys to image_id before saving
        normalized_proxy_results = rename_key_in_json(proxy_matching_results, "imageId", "image_id")

        # Save proxy matching results with normalized keys
        with open(proxy_output_path, 'w') as f:
            json.dump(normalized_proxy_results, f, indent=2)
        
        log(f"Proxy method proposal complete. Generated proposals for {len(proxy_matching_results)} virtual objects.")
        
        # Add to result
        result["proxy_matching"] = {
            "count": len(proxy_matching_results),
            "database_path": proxy_output_path,
            "matching_results": proxy_matching_results
        }
    
    # Process property rating results if available
    if environment_image_base64_list and haptic_annotation_json:
        log("Processing completed property rating results")
        property_rating_results = concurrent_results.get("property_rating_result", [])
        
        # Save property rating results
        output_dir = os.path.join(script_dir, "output")
        property_rating_output_path = os.path.join(output_dir, "property_rating_results.json")
        
        # Ensure output directory exists
        os.makedirs(output_dir, exist_ok=True)
        
        # Save property rating results
        with open(property_rating_output_path, 'w') as f:
            json.dump(property_rating_results, f, indent=2)
        
        log(f"Property rating complete. Generated ratings for {len(property_rating_results)} virtual objects.")
        
        # Calculate final scores and update proxy matching results
        if len(property_rating_results) > 0 and len(proxy_matching_results) > 0:
            log("Calculating final scores for proxy matching results")
            updated_proxy_results = calculate_final_scores(property_rating_results, proxy_matching_results)
            
            # Save updated proxy matching results
            with open(proxy_output_path, 'w') as f:
                json.dump(updated_proxy_results, f, indent=2)
            
            log("Updated proxy matching results with final scores")
        
        # Add to result
        result["property_rating"] = {
            "count": len(property_rating_results),
            "database_path": property_rating_output_path,
            "rating_results": property_rating_results
        }
    
    # Process relationship rating results if available
    if environment_image_base64_list and haptic_annotation_json:
        log("Processing completed relationship rating results")
        relationship_rating_results = concurrent_results.get("relationship_rating_result", [])
        
        # Save relationship rating results
        output_dir = os.path.join(script_dir, "output")
        relationship_rating_output_path = os.path.join(output_dir, "relationship_rating_results.json")
        
        # Ensure output directory exists
        os.makedirs(output_dir, exist_ok=True)
        
        # Save relationship rating results
        with open(relationship_rating_output_path, 'w') as f:
            json.dump(relationship_rating_results, f, indent=2)
        
        log(f"Relationship rating complete. Generated ratings for {len(relationship_rating_results)} object pairs.")
        
        # Add to result
        result["relationship_rating"] = {
            "count": len(relationship_rating_results),
            "database_path": relationship_rating_output_path,
            "rating_results": relationship_rating_results
        }
    
    # Process substrate utilization results if available
    if environment_image_base64_list and haptic_annotation_json:
        log("Processing completed substrate utilization results")
        substrate_utilization_results = concurrent_results.get("substrate_utilization_result", [])
        
        # Save substrate utilization results
        output_dir = os.path.join(script_dir, "output")
        substrate_utilization_output_path = os.path.join(output_dir, "substrate_utilization_results.json")
        
        # Ensure output directory exists
        os.makedirs(output_dir, exist_ok=True)
        
        # Save substrate utilization results
        with open(substrate_utilization_output_path, 'w') as f:
            json.dump(substrate_utilization_results, f, indent=2)
        
        log(f"Substrate utilization method generation complete. Generated methods for {len(substrate_utilization_results)} contact-substrate pairs.")
        
        # Add to result
        result["substrate_utilization"] = {
            "count": len(substrate_utilization_results),
            "database_path": substrate_utilization_output_path,
            "utilization_results": substrate_utilization_results
        }
    
    # Print final result as JSON
    print(json.dumps(result, indent=2))
        
except Exception as e:
    log(f"Error in processing: {e}")
    import traceback
    log(traceback.format_exc())
    print(json.dumps({"status": "error", "message": str(e)}))