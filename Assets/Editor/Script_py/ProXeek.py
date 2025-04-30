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
    You are a VR haptic proxy finder. Your task is to analyze images of a user's physical environment and identify objects that could serve as haptic proxies for virtual objects in VR.

    A good haptic proxy should:
    1. Have similar physical properties (shape, size, weight) to the virtual object
    2. Be easily accessible to the user
    3. Be safe to interact with

    For each potential proxy you identify:
    - Describe its location in the image
    - Explain why it would make a good proxy for the specified virtual object
    - Note any limitations or considerations for using it

    I will provide multiple images of the same environment from different angles. Consider all images when making your recommendations.

    Be specific and practical in your recommendations.
    """

    # Create the messages
    messages = [SystemMessage(content=system_prompt)]

    # Add the haptic requirements
    user_prompt = f"Based on the following requirements for a virtual object: {haptic_requirements}\n\nIdentify potential haptic proxies in the attached images of my physical environment."

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