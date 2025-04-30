import os
import sys
import json
from dotenv import load_dotenv
from langchain_core.messages import HumanMessage, SystemMessage
from langchain_openai import ChatOpenAI


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
        log(f"Loaded parameters: {params}")
        prompt = params.get('prompt')
        if not prompt:
            prompt = "What's the meaning of life?"
            log(f"No prompt found in parameters, using default: {prompt}")
        else:
            log(f"Using prompt from parameters: {prompt}")
    except Exception as e:
        log(f"Error reading parameters file: {e}")
        prompt = "What's the meaning of life?"
else:
    # Default when running from Unity Editor
    log("No parameters file provided, using default prompt")
    prompt = "What's the meaning of life?"

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

    # Create the prompt
    log(f"Creating prompt with content: {prompt}")
    prompt_system = SystemMessage(content=prompt)

    # Get the response
    log("Sending request to LLM")
    response = proxy_picker_llm.invoke([prompt_system])

    # Print the response (this will be captured by the server)
    log("Received response from LLM")
    print(response.content)
except Exception as e:
    log(f"Error in LLM processing: {e}")
    print(f"Error: {str(e)}")