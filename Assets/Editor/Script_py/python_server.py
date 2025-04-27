from flask import Flask, request, jsonify
import subprocess
import os
import sys
import json

app = Flask(__name__)

# Path to your Unity project's Python scripts
SCRIPTS_PATH = r"C:\Users\aaron\Documents\GitHub\ProXeek\Assets\Editor\Script_py"


@app.route('/run_python', methods=['POST'])
def run_python():
    try:
        data = request.json

        if not data or 'action' not in data or data['action'] != 'run_script':
            return jsonify({'error': 'Invalid request format'}), 400

        script_name = data.get('script_name', 'new_python_script.py')
        params = data.get('params', {})

        # Full path to the script
        script_path = os.path.join(SCRIPTS_PATH, script_name)

        if not os.path.exists(script_path):
            return jsonify({'error': f'Script not found: {script_name}'}), 404

        # Create a temporary JSON file with parameters
        params_path = os.path.join(SCRIPTS_PATH, 'temp_params.json')
        with open(params_path, 'w') as f:
            json.dump(params, f)

        # Run the Python script with the parameters file path as an argument
        result = subprocess.run(
            [sys.executable, script_path, params_path],
            capture_output=True,
            text=True
        )

        # Clean up the temporary file
        if os.path.exists(params_path):
            os.remove(params_path)

        # Check for errors
        if result.returncode != 0:
            return jsonify({
                'status': 'error',
                'stdout': result.stdout,
                'stderr': result.stderr,
                'code': result.returncode
            }), 500

        # Return the output
        return jsonify({
            'status': 'success',
            'output': result.stdout
        })

    except Exception as e:
        return jsonify({'error': str(e)}), 500


if __name__ == '__main__':
    app.run(host='0.0.0.0', port=5000, debug=True)