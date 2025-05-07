import os
import sys
import json
import base64
from io import BytesIO
from dotenv import load_dotenv
from langchain_core.messages import HumanMessage, SystemMessage
from langchain_openai import ChatOpenAI
from PIL import Image


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

        haptic_requirements = params.get('hapticRequirements',
                                         "Identify objects that could serve as haptic proxies in VR")
        image_base64_list = params.get('imageBase64List', [])

        if not image_base64_list:
            log("No image data found in parameters")
            prompt = haptic_requirements
        else:
            log(f"Found {len(image_base64_list)} images")
            # We'll use the images with the prompt
    except Exception as e:
        log(f"Error reading parameters file: {e}")
        haptic_requirements = "Identify objects that could serve as haptic proxies in VR"
        image_base64_list = []
else:
    # Default when running from Unity Editor
    log("No parameters file provided, using default prompt")
    haptic_requirements = "Identify objects that could serve as haptic proxies in VR"
    image_base64_list = []

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

try:
    # Initialize the LLM
    log("Initializing ChatOpenAI")
    proxy_picker_llm = ChatOpenAI(
        model="gpt-4o-2024-11-20",
        temperature=0.1,
        base_url="https://reverse.onechats.top/v1",
        api_key=api_key
    )

    # Create the system prompt
    system_prompt = """
    You are a haptic proxy picker. Your primary goal is to select suitable physical proxies from the images of the real-world surroundings of a VR user so that the user experiences the intended haptic feedback when interacting with target virtual objects.

    Annotation of Input JSON File:
    -summary: Overall description about the current VR scene.
    
    -nodeAnnotations.objectName: The target virtual object in the VR scene for which you should propose a haptic proxy from surroundings.
    -nodeAnnotations.isDirectContacted: Whether this virtual object would be directly contacted or not.
    -nodeAnnotations.description: Overall description about the usage of this virtual object in the current VR scene.
    -nodeAnnotations.engagementLevel: 0: low engagement, 1: medium engagement, 2: high engagement. It basically reflects how often the VR users will interact with this virtual object.
    -nodeAnnotations.snapshotPath: The path where the snapshot of this target virtual object is stored.
    -nodeAnnotations.inertia: Highly expected haptic feedback, if any, regarding the target virtual object's mass and gravity center.
    -nodeAnnotations.interactivity: Highly expected haptic feedback, if any, regarding the target virtual object's interactable features.
    -nodeAnnotations.outline: Highly expected haptic feedback, if any, regarding the target virtual object's shape and size.
    -nodeAnnotations.texture: Highly expected haptic feedback, if any, regarding the target virtual object's surface texture.
    -nodeAnnotations.hardness: Highly expected haptic feedback, if any, regarding the target virtual object's hardness of contact or flexible parts.
    -nodeAnnotations.temperature: Highly expected haptic feedback, if any, regarding the perceived temperature of the contact points on the target virtual object.
    -nodeAnnotations.inertiaValue: From 0 to 1, how important the inertia property is.
    -nodeAnnotations.interactivity: From 0 to 1, how important the interactivity property is.
    -nodeAnnotations.outline: From 0 to 1, how important the outline property is.
    -nodeAnnotations.texture: From 0 to 1, how important the texture property is.
    -nodeAnnotations.hardness: From 0 to 1, how important the hardness property is.
    -nodeAnnotations.temperature: From 0 to 1, how important the temperature property is.
    
    -relationshipAnnotations.contactObject: The virtual object which a VR user direct contact with.
    -relationshipAnnotations.substrateObject: The virtual object which a direct contacted virtual object will interact or collide with.
    -relationshipAnnotations.annotationText: Anticipated haptic feedback transmitted through the contactObject when it comes into contact with the substrateObject.
    
    -groups.title: The name of grouped target virtual objects
    -groups.objectNames: The names of target virtual objects
    -groups.objectVectors.objectA: The name of objectA
    -groups.objectVectors.objectB: The name of objectB
    -groups.objectVectors.vector: The coordinate of the vector pointing from objectA to objectB.
    -groups.objectVectors.distance: The distance between objectA and objectB with meters as unit.
    -groups.objectVectors.additionalViewAngles: Different angles of snapshots of the grouped virtual objects in VR scene.

    Proxy Picking Instructions:
    1. Base Your Decisions on the Provided Data
        *Construct the interaction scenario and anticipate the expected haptic feedback by reviewing the provided images, text descriptions, and usage instructions.
        *Then, think in reverse: envision which physical proxies from the environment can supply the needed haptic sensations.
    2. Think Differently by Contact Type
        *Direct Contact Objects
            -These are virtual objects the user's body directly contacts (e.g., tennis racket, shovel, chair)
            -Strive for close matching across key haptic dimensions (shape, weight, texture, hardness), because contact is immediate.
            -If “highly expected haptic feedback” is specified, prioritize simulating those properties.
        *Tool-Mediated Objects
            -These objects interact with the user indirectly via another tool (e.g., a golf ball being struck by a club).
            -Be more flexible and creative when picking a proxy, as long as the user perceives the correct collisions, vibrations or force through the direct contact tool (e.g., a christmas tree could be a haptic proxy of a ping pang ball since the bat normally end up colliding with the tree with every swing; the scissors placed in a pen holder can serve as the haptic proxy for the lock when simulating the feedback of prying the lock open with a crowbar).
    3. Choose with Focus
        *"Highly expected haptic feedback" indicates which properties are especially prioritized by the VR developer. Although you should consider every property that might matter for immersion, prioritize these highlighted properties first if there is a trade-off.
        *A "no further expectation" means there is no specific emphasis from the developer--however, you must still propose a proxy for that virtual object.
    4. Consider Multi-Object Interactions
        *Deduce the mentioned interaction pairs and how they might physically collide, transfer, interact with and so on.
        *Think carefully about any haptic feedback that arises from these interactions.
    
    Final Output Requirements: 
    1. Assign the most suitable physical object to each target virtual object (one proxy per virtual item).
    2. Specify the location of each chosen haptic proxy (i.e., where it is found in the provided images)
    3. Justify your proxy selection for each virtual object.
    4. Describe how to hold or manipulate the chosen proxies so it simulate the expected haptic feedback.
    """

    # Create the messages
    messages = [SystemMessage(content=system_prompt)]

    # Add the haptic requirements
    user_prompt = f"Images of VR user's surroundings:"

    # If we have images, add them to the message
    if image_base64_list:
        log(f"Adding {len(image_base64_list)} images to message")

        # Create content list with text first
        content_list = [{"type": "text", "text": user_prompt}]

        # Add each image
        for i, image_base64 in enumerate(image_base64_list):
            content_list.append({
                "type": "image_url",
                "image_url": {
                    "url": f"data:image/jpeg;base64,{image_base64}",
                    "detail": "high"
                }
            })
            log(f"Added image {i + 1}/{len(image_base64_list)}")

        messages.append(HumanMessage(content=content_list))
    else:
        log("No images, using text-only prompt")
        messages.append(HumanMessage(content=user_prompt))

    # Get the response
    log("Sending request to LLM")
    response = proxy_picker_llm.invoke(messages)

    # Print the response (this will be captured by the server)
    log("Received response from LLM")
    print(response.content)
except Exception as e:
    log(f"Error in LLM processing: {e}")
    print(f"Error: {str(e)}")