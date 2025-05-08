from flask import Flask, request, jsonify
import subprocess
import os
import sys
import json
import traceback

app = Flask(__name__)

# Increase maximum content length to handle multiple images
app.config['MAX_CONTENT_LENGTH'] = 100 * 1024 * 1024  # Allow up to 100MB payloads

# Path to your Unity project's Python scripts
SCRIPTS_PATH = r"C:\Users\aaron\Documents\GitHub\ProXeek\Assets\Editor\Script_py"


@app.route('/run_python', methods=['POST'])
def run_python():
    try:
        print("Received request of size:", len(request.data))
        data = request.json

        if not data:
            print("No JSON data received")
            return jsonify({'status': 'error', 'output': 'No JSON data received'}), 400

        print("Parsed JSON data keys:", data.keys())

        if 'action' not in data or data['action'] != 'run_script':
            print("Invalid request format - missing 'action' or not 'run_script'")
            return jsonify({'status': 'error', 'output': 'Invalid request format'}), 400

        script_name = data.get('script_name', 'ProXeek.py')
        params = data.get('params', {})

        print(f"Script name: {script_name}")
        if 'environmentImageBase64List' in params:
            print(f"Received {len(params['environmentImageBase64List'])} environment images")
        if 'virtualObjectSnapshots' in params:
            print(f"Received {len(params['virtualObjectSnapshots'])} virtual object snapshots")
        if 'arrangementSnapshots' in params:
            print(f"Received {len(params['arrangementSnapshots'])} arrangement snapshots")

        # Full path to the script
        script_path = os.path.join(SCRIPTS_PATH, script_name)

        if not os.path.exists(script_path):
            print(f"Script not found: {script_path}")
            return jsonify({'status': 'error', 'output': f'Script not found: {script_name}'}), 404

        # Create a temporary JSON file with parameters
        params_path = os.path.join(SCRIPTS_PATH, 'temp_params.json')
        with open(params_path, 'w') as f:
            json.dump(params, f)

        print(f"Running script: {sys.executable} {script_path} {params_path}")

        # Run the Python script with the parameters file path as an argument
        result = subprocess.run(
            [sys.executable, script_path, params_path],
            capture_output=True,
            text=True,
            timeout=300  # Increase timeout for processing multiple images
        )

        # Clean up the temporary file
        if os.path.exists(params_path):
            os.remove(params_path)

        # Check for errors
        if result.returncode != 0:
            print(f"Script execution failed with code {result.returncode}")
            print(f"STDOUT: {result.stdout}")
            print(f"STDERR: {result.stderr}")

            return jsonify({
                'status': 'error',
                'output': f"Error executing script:\n{result.stderr}\n{result.stdout}"
            }), 500

        # Return the output
        print(f"Script executed successfully, output length: {len(result.stdout)}")
        return jsonify({
            'status': 'success',
            'output': result.stdout
        })

    except Exception as e:
        print(f"Server error: {str(e)}")
        traceback.print_exc()
        return jsonify({'status': 'error', 'output': f"Server error: {str(e)}"}), 500


if __name__ == '__main__':
    print(f"Starting Python server on port 5000")
    print(f"Scripts path: {SCRIPTS_PATH}")
    app.run(host='0.0.0.0', port=5000, debug=True)